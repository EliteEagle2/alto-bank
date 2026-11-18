using AltoBank.Data;
using AltoBank.Models;
using Microsoft.EntityFrameworkCore;

namespace AltoBank.Services;

/// <summary>What one loan's scheduler step did (for the scheduler's summary).</summary>
public record LoanCycleOutcome(int AutopaysMade, int AutopaysFailed, int LateFees);

/// <summary>
/// LOANS (personal and auto): the money side. A loan account's Balance is NEGATIVE while money is owed, and always equals
/// -(Loan.PrincipalRemaining + Loan.FeesOwed). Every movement runs in InTransactionAsync (one DB transaction, the
/// Balance concurrency check, an AuditLog row), and both sides of a movement get a transaction row:
///   accept an offer  → LoanDisbursement on the loan  + LoanProceeds in checking
///   a payment        → TransferOut from checking/savings + LoanPayment on the loan (+ the interest it pays, as an
///                      InterestCharge row on the loan, so the loan's running balance always adds up)
///   late fee         → LateFee on the loan (FeesOwed goes up)
/// The scheduler (RunLoanCycleAsync) does autopay on the due date, the late fee once a payment is more than the grace
/// days late, and a reminder 5 days before a due date.
/// </summary>
public partial class BankService
{
    public const int LoanOfferValidDays = 30;

    // ---------------------------------------------------------------- applying (called from SubmitApplicationAsync)

    /// <summary>The loan rules a submitted application must meet (the controller checks them too; the API path can't skip them).</summary>
    private async Task<string?> CheckLoanApplicationAsync(string userId, AccountApplication app)
    {
        if (app.LoanKind is not LoanKind kind) return "Choose a personal or an auto loan.";
        var (min, max, minTerm, maxTerm) = LoanMath.Limits(kind);
        if (app.LoanAmount is not decimal amount || amount < min || amount > max)
            return $"{(kind == LoanKind.Auto ? "Auto" : "Personal")} loans are {min:C0} to {max:C0}.";
        if (decimal.Round(amount, 0) != amount) return "Enter the loan amount in whole dollars.";
        if (app.LoanTermMonths is not int term || !LoanMath.Terms(kind).Contains(term))
            return $"Choose a term between {minTerm} and {maxTerm} months.";
        if (app.LoanPurpose == null || !ApplicationOptions.LoanPurposes(kind).Contains(app.LoanPurpose)) return "Choose what the loan is for.";
        var thisYear = BankTime.Today.Year;
        if (kind == LoanKind.Auto && (app.VehicleYear is not int y || y < thisYear - 25 || y > thisYear + 1
                                      || string.IsNullOrWhiteSpace(app.VehicleMake) || app.VehicleMake.Length > 30
                                      || string.IsNullOrWhiteSpace(app.VehicleModel) || app.VehicleModel.Length > 40))
            return $"Enter the vehicle's year ({thisYear - 25} to {thisYear + 1}), make and model.";
        if (app.DisburseToAccountId is not int toId
            || await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == toId && a.UserId == userId) is not { AccountType: AccountType.Checking, Status: not AccountStatus.Closed })
            return "Choose the Alto checking account the money should go to.";
        var open = await _db.AccountApplications.AsNoTracking()
            .Where(a => a.UserId == userId && a.AccountType == AccountType.Loan && (a.Status == ApplicationStatus.Pending || a.Status == ApplicationStatus.Offered))
            .ToListAsync();
        if (open.Any(a => LoanInProgress(a, BankTime.Today)))
            return "You already have a loan application in progress. Finish it (or decline the offer) first.";
        return null;
    }

    /// <summary>What Alto knows about a customer for the simulated loan decision (no credit bureau).</summary>
    private async Task<LoanHistory> LoanHistoryAsync(string userId, int graceDays)
    {
        var c = await CreditHistoryAsync(userId);
        var today = BankTime.Today;
        var dues = await _db.Loans.AsNoTracking().Where(l => l.UserId == userId && l.Status == LoanStatus.Active)
            .Select(l => l.NextDueDate).ToListAsync();
        return new LoanHistory(c.DaysAsCustomer, c.DepositBalance, c.HasFrozenAccount,
            HasOverdueLoan: dues.Any(d => d is DateOnly due && today > due.AddDays(graceDays)));
    }

    /// <summary>Sets the simulated decision on a loan application: an offer, a pending review (with the terms), or a denial.</summary>
    private async Task<BankResult> DecideLoanAsync(string userId, AccountApplication app, BankSetting settings)
    {
        if (await CheckLoanApplicationAsync(userId, app) is { } problem) return new BankResult(false, problem);
        var d = LoanDecisionRules.Decide(app.LoanKind!.Value, app.LoanAmount!.Value, app.LoanTermMonths!.Value, app.IncomeRange,
                                         await LoanHistoryAsync(userId, settings.LoanGraceDays), settings);
        app.LoanRateTier = d.Tier;
        app.LoanAprPercent = d.AprPercent;
        app.LoanMonthlyPayment = d.MonthlyPayment;
        app.DecisionNote = d.Note;
        switch (d.Outcome)
        {
            case CreditOutcome.Approved:
                app.Status = ApplicationStatus.Offered;
                app.OfferedAt = app.ReviewedAt = DateTime.UtcNow;
                app.ReviewedBy = "Automatic decision (demo, no credit check)";
                return new BankResult(true, "Your loan offer is ready. Review it and accept to get your money.");
            case CreditOutcome.Denied:
                app.Status = ApplicationStatus.Denied;
                app.DenialReason = d.Note;
                app.ReviewedAt = DateTime.UtcNow;
                app.ReviewedBy = "Automatic decision (demo, no credit check)";
                return new BankResult(true, "We've made a decision on your application.");
            default:
                app.Status = ApplicationStatus.Pending;
                return new BankResult(true, "Thanks! Your application was submitted and is being reviewed.");
        }
    }

    // ---------------------------------------------------------------- the offer

    /// <summary>Pending, or an offer that hasn't expired: only one loan application at a time. (An expired offer frees the customer to apply again.)</summary>
    public static bool LoanInProgress(AccountApplication a, DateOnly today) =>
        a.AccountType == AccountType.Loan
        && (a.Status == ApplicationStatus.Pending || a.Status == ApplicationStatus.Offered && today <= OfferExpiresOn(a));

    public static DateOnly OfferExpiresOn(AccountApplication app) =>
        BankTime.DateOf(DateTime.SpecifyKind(app.OfferedAt ?? app.SubmittedAt, DateTimeKind.Utc)).AddDays(LoanOfferValidDays);

    /// <summary>
    /// ACCEPT: opens the Loan account, sends the money to the customer's checking (a row on both sides), and starts the
    /// schedule (first payment one month from today). The unique index on Loan.ApplicationId means two clicks at the
    /// same moment can't create two loans.
    /// </summary>
    public async Task<(BankResult Result, Account? LoanAccount)> AcceptLoanOfferAsync(string userId, int applicationId, int toAccountId)
    {
        Account? opened = null;
        var result = await InTransactionAsync(async () =>
        {
            var app = await _db.AccountApplications.FirstOrDefaultAsync(a => a.Id == applicationId && a.UserId == userId && a.AccountType == AccountType.Loan);
            if (app == null) return new BankResult(false, "Offer not found.");
            if (app.Status != ApplicationStatus.Offered)
                return new BankResult(false, app.Status == ApplicationStatus.Approved ? "You already accepted this offer." : "This offer can't be accepted.");
            var today = BankTime.Today;
            if (today > OfferExpiresOn(app)) return new BankResult(false, $"This offer expired on {OfferExpiresOn(app):MMM d, yyyy}. You're welcome to apply again.");
            var to = await GetAccountAsync(toAccountId, userId);
            if (to is not { AccountType: AccountType.Checking }) return new BankResult(false, "Choose one of your Alto checking accounts for the money.");
            if (CanReceive(to) is { } blocked) return blocked;

            var amount = app.LoanAmount!.Value;
            var apr = app.LoanAprPercent!.Value;
            var term = app.LoanTermMonths!.Value;
            var account = new Account
            {
                UserId = userId, AccountType = AccountType.Loan, AccountNumber = await GenerateAccountNumberAsync(),
                Nickname = app.LoanKind == LoanKind.Auto ? "Auto Loan" : "Personal Loan",
                OpenedAt = DateTime.UtcNow, Status = AccountStatus.Active
            };
            _db.Accounts.Add(account);
            await _db.SaveChangesAsync();

            var firstDue = today.AddMonths(1);
            if (firstDue.Day > 28) firstDue = new DateOnly(firstDue.Year, firstDue.Month, 28);   // due on the same day every month
            var loan = new Loan
            {
                AccountId = account.Id, UserId = userId, ApplicationId = app.Id,
                Kind = app.LoanKind!.Value, Purpose = app.LoanPurpose,
                VehicleYear = app.VehicleYear, VehicleMake = app.VehicleMake, VehicleModel = app.VehicleModel,
                Principal = amount, AprPercent = apr, TermMonths = term,
                MonthlyPayment = LoanMath.MonthlyPayment(amount, apr, term), RateTier = app.LoanRateTier,
                DisbursedAt = DateTime.UtcNow, DisbursedToAccountId = to.Id, FirstDueDate = firstDue,
                PrincipalRemaining = amount, NextDueDate = firstDue
            };
            _db.Loans.Add(loan);

            account.Balance -= amount;
            to.Balance += amount;
            var loanLeg = AddTransaction(account, TransactionType.LoanDisbursement, amount, $"Loan funds sent to {to.DisplayName} ••{to.Last4}", counterpartyId: to.Id);
            var depositLeg = AddTransaction(to, TransactionType.LoanProceeds, amount, $"{account.DisplayName} ••{account.Last4} funds", counterpartyId: account.Id);
            await _db.SaveChangesAsync();
            LinkLegs(loanLeg, depositLeg);

            app.Status = ApplicationStatus.Approved;
            app.AccountId = account.Id;
            app.OfferRespondedAt = DateTime.UtcNow;
            _alerts.Add(userId, AlertType.LoanUpdate, "Your loan is funded",
                $"{amount:C} from your {account.DisplayName} is in {to.DisplayName} ••{to.Last4}. Your first payment of {loan.MonthlyPayment:C} is due {firstDue:MMM d}.",
                $"/accounts/{account.Id}");
            _audit.Add("Loan.Disburse", $"Loan ••{account.Last4}: {amount:C} at {apr:0.00}% for {term} months, sent to ••{to.Last4}",
                       "Account", account.Id.ToString(), after: new { applicationId = app.Id, amount, apr, term, monthly = loan.MonthlyPayment, to = to.Id, firstDue });
            await _db.SaveChangesAsync();
            opened = account;
            return new BankResult(true, $"Your loan is funded: {amount:C} is in {to.DisplayName} ••{to.Last4}. First payment {loan.MonthlyPayment:C} due {firstDue:MMM d}.");
        });
        return (result, result.Success ? opened : null);
    }

    public Task<BankResult> DeclineLoanOfferAsync(string userId, int applicationId) =>
        InTransactionAsync(async () =>
        {
            var app = await _db.AccountApplications.FirstOrDefaultAsync(a => a.Id == applicationId && a.UserId == userId && a.AccountType == AccountType.Loan);
            if (app == null) return new BankResult(false, "Offer not found.");
            if (app.Status != ApplicationStatus.Offered) return new BankResult(false, "This offer can't be declined.");
            app.Status = ApplicationStatus.OfferDeclined;
            app.OfferRespondedAt = DateTime.UtcNow;
            _audit.Add("Loan.DeclineOffer", $"Declined loan offer #{app.Id} ({app.LoanAmount:C})", "Application", app.Id.ToString(),
                       new { status = "Offered" }, new { status = "OfferDeclined" });
            await _db.SaveChangesAsync();
            return new BankResult(true, "You declined the offer. Nothing was borrowed. You can apply again any time.");
        });

    // ---------------------------------------------------------------- payments

    public Task<BankResult> PayLoanAsync(string userId, int loanAccountId, int fromAccountId, LoanPaymentKind kind, decimal? extraAmount, DateOnly? forDueDate = null) =>
        InTransactionAsync(async () => await PayLoanCoreAsync(userId, loanAccountId, fromAccountId, kind, extraAmount, autopay: false, BankTime.Today, forDueDate));

    private async Task<BankResult> PayLoanCoreAsync(string userId, int loanAccountId, int fromAccountId, LoanPaymentKind kind, decimal? extraAmount, bool autopay, DateOnly today,
                                                    DateOnly? forDueDate = null)
    {
        var account = await GetAccountAsync(loanAccountId, userId);   // ownership check
        if (account is not { IsLoan: true }) return new(false, "Loan not found.");
        var loan = await _db.Loans.FirstOrDefaultAsync(l => l.AccountId == account.Id);
        if (loan == null) return new(false, "Loan not found.");
        if (loan.Status == LoanStatus.PaidOff || account.Status == AccountStatus.Closed) return new(false, "This loan is paid off. There's nothing to pay.");
        var from = await GetAccountAsync(fromAccountId, userId);
        if (from == null) return new(false, "Choose one of your checking or savings accounts.");
        if (CanSend(from) is { } blocked) return blocked;

        var q = LoanQuote.For(loan, today);
        decimal amount, interest, principal, fees;
        switch (kind)
        {
            case LoanPaymentKind.Scheduled:
                if (forDueDate is DateOnly asked && asked != loan.NextDueDate)
                    return new(false, $"Your {asked:MMM d} payment was already made, so nothing was paid again. Your next payment is due {loan.NextDueDate:MMM d}.");
                if (!q.CanPayScheduled) return new(false, ScheduledNotOpenYet(loan, q));
                (amount, interest, principal, fees) = (q.ScheduledAmount, q.InstallmentInterest, q.InstallmentPrincipal, loan.FeesOwed);
                break;
            case LoanPaymentKind.ExtraPrincipal:
                amount = extraAmount ?? 0m;
                if (CheckAmount(amount) is { } invalid) return invalid;
                if (amount >= loan.PrincipalRemaining)
                    return new(false, $"That would pay off the whole loan ({loan.PrincipalRemaining:C} principal). Choose \"Pay off the loan\" instead, so the interest to today is included.");
                (interest, principal, fees) = (0m, amount, 0m);
                break;
            case LoanPaymentKind.Payoff:
                (amount, interest, principal, fees) = (q.PayoffAmount, q.AccruedInterest, loan.PrincipalRemaining, loan.FeesOwed);
                break;
            default:
                return new(false, "Choose what you want to pay.");
        }
        // No daily transfer limit: paying your own loan keeps the money inside Alto, and a $5,000 limit would make a
        // larger loan impossible to pay off in one go (see GetDailyTransferUsageAsync).
        if (amount > from.Balance) return new(false, $"Insufficient funds. {from.DisplayName} has {from.Balance:C} available.");

        var installment = loan.PaymentsMade + 1;
        var dueDate = loan.NextDueDate;
        if (interest > 0)
        {
            account.Balance -= interest;
            AddTransaction(account, TransactionType.InterestCharge, interest,
                kind == LoanPaymentKind.Payoff ? $"Interest to {today:MMM d} ({q.DaysAccrued} days)" : $"Interest for the {dueDate:MMM d} payment",
                memo: $"{loan.AprPercent:0.00}% APR");
        }
        from.Balance -= amount;
        account.Balance += amount;
        var outLeg = AddTransaction(from, TransactionType.TransferOut, amount, $"Payment to {account.DisplayName} ••{account.Last4}", counterpartyId: account.Id);
        outLeg.Category = TransactionCategory.DebtPayments;   // the checking side of paying your own loan
        var label = kind switch
        {
            LoanPaymentKind.Payoff => "Loan payoff",
            LoanPaymentKind.ExtraPrincipal => "Extra payment toward principal",
            _ => autopay ? $"Autopay, {dueDate:MMM d} payment" : $"Payment, {dueDate:MMM d} installment"
        };
        var inLeg = AddTransaction(account, TransactionType.LoanPayment, amount, $"{label} from {from.DisplayName} ••{from.Last4}", counterpartyId: from.Id);
        await _db.SaveChangesAsync();
        LinkLegs(outLeg, inLeg);

        loan.PrincipalRemaining -= principal;
        loan.FeesOwed -= fees;
        if (kind == LoanPaymentKind.Scheduled)
        {
            loan.PaymentsMade++;
            loan.NextDueDate = dueDate!.Value.AddMonths(1);
        }
        _db.LoanPayments.Add(new LoanPayment
        {
            LoanId = loan.Id, Kind = kind, ByAutopay = autopay,
            InstallmentNumber = kind == LoanPaymentKind.Scheduled ? installment : null,
            DueDate = kind == LoanPaymentKind.Scheduled ? dueDate : null,
            Amount = amount, Interest = interest, Principal = principal, Fees = fees,
            PrincipalAfter = loan.PrincipalRemaining, FromAccountId = from.Id, TransactionId = inLeg.Id
        });
        var paidOff = loan.PrincipalRemaining == 0m && loan.FeesOwed == 0m;
        if (paidOff) MarkPaidOff(loan, account, _alerts);
        // The loan's balance and its principal + fees must always agree; if not, roll everything back.
        if (-account.Balance != loan.PrincipalRemaining + loan.FeesOwed)
            throw new InvalidOperationException($"Loan {loan.Id}: balance {account.Balance} doesn't match principal {loan.PrincipalRemaining} + fees {loan.FeesOwed}.");

        _audit.Add(autopay ? "Loan.Autopay" : kind == LoanPaymentKind.Payoff ? "Loan.Payoff" : "Loan.Payment",
                   $"Paid {amount:C} to loan ••{account.Last4} from ••{from.Last4} ({kind})", "Account", account.Id.ToString(),
                   after: new { amount, kind = kind.ToString(), interest, principal, fees, principalLeft = loan.PrincipalRemaining, from = from.Id, nextDue = loan.NextDueDate });
        await _db.SaveChangesAsync();
        return new(true, paidOff
            ? $"Paid {amount:C}. Your {account.DisplayName} is paid in full. Congratulations!"
            : kind == LoanPaymentKind.ExtraPrincipal
                ? $"Paid {amount:C} toward principal. You now owe {loan.PrincipalRemaining:C}, and your loan ends sooner."
                : $"Paid {amount:C}. Next payment {LoanQuote.For(loan, today).ScheduledAmount:C} due {loan.NextDueDate:MMM d}.");
    }

    /// <summary>Why the monthly payment can't be made yet (shown on Make a payment and when it's refused).</summary>
    public static string ScheduledNotOpenYet(Loan loan, LoanQuote q) => loan.PaymentsMade == 0
        ? $"Your first payment is due {loan.NextDueDate:MMM d}. You can make it from {q.OpensOn:MMM d}, or pay extra toward principal now."
        : $"Your {loan.NextDueDate!.Value.AddMonths(-1):MMM d} payment is made. The next one ({loan.NextDueDate:MMM d}) can be made from {q.OpensOn:MMM d}, or pay extra toward principal now.";

    /// <summary>Last dollar repaid: the loan is PaidOff and its account Closed (history and the paid-in-full letter stay).</summary>
    public static void MarkPaidOff(Loan loan, Account account, IAlertService alerts)
    {
        loan.Status = LoanStatus.PaidOff;
        loan.PaidOffAt = DateTime.UtcNow;
        loan.NextDueDate = null;
        loan.AutopayEnabled = false;
        loan.AutopayFromAccountId = null;
        account.Status = AccountStatus.Closed;
        account.StatusReason = "Paid in full";
        account.StatusChangedAt = account.ClosedAt = DateTime.UtcNow;
        alerts.Add(loan.UserId, AlertType.LoanUpdate, "Loan paid in full",
            $"Your {account.DisplayName} ••{account.Last4} is paid in full. Your paid-in-full letter is ready.", $"/loans/{account.Id}/letter");
    }

    // ---------------------------------------------------------------- scheduler

    /// <summary>
    /// One loan, for the bank day <paramref name="today"/> (idempotent):
    ///   1. AUTOPAY each due installment (due date reached, not tried yet for that date), oldest first.
    ///   2. LATE FEE once for each due date more than the grace days behind (Loan.LateFeeForDueDate remembers the last one).
    ///   3. A reminder 5 days before the due date when autopay is off.
    /// Reads first, so an idle tick never takes the database write lock.
    /// </summary>
    public async Task<LoanCycleOutcome> RunLoanCycleAsync(int loanId, DateOnly today)
    {
        int made = 0, failed = 0, fees = 0;
        _db.ChangeTracker.Clear();
        var settings = await GetSettingsAsync();
        var peek = await _db.Loans.AsNoTracking().FirstOrDefaultAsync(l => l.Id == loanId);
        if (peek is not { Status: LoanStatus.Active, NextDueDate: DateOnly due }) return new LoanCycleOutcome(0, 0, 0);

        var autopayDue = peek.AutopayEnabled && due <= today && peek.AutopayAttemptedFor != due;
        var lastFee = peek.LateFeeForDueDate;
        var feeDue = today > (lastFee is DateOnly lf && lf >= due ? lf.AddMonths(1) : due).AddDays(settings.LoanGraceDays);
        if (!autopayDue && !feeDue)
        {
            await LoanReminderAsync(peek, today);
            return new LoanCycleOutcome(0, 0, 0);
        }

        for (var i = 0; i < 12; i++)
        {
            var r = await LoanAutopayIfDueAsync(loanId, today);
            if (r == null) break;
            if (r == true) made++; else { failed++; break; }
        }
        for (var i = 0; i < 12; i++)
        {
            var fee = await InTransactionAsync(async () => await LoanLateFeeIfDueAsync(loanId, today, settings));
            if (!fee.Success) break;
            fees++;
        }
        return new LoanCycleOutcome(made, failed, fees);
    }

    // true = paid, false = failed, null = nothing to do
    private async Task<bool?> LoanAutopayIfDueAsync(int loanId, DateOnly today)
    {
        _db.ChangeTracker.Clear();
        var loan = await _db.Loans.AsNoTracking().Include(l => l.Account).FirstOrDefaultAsync(l => l.Id == loanId);
        if (loan is not { Status: LoanStatus.Active, AutopayEnabled: true, NextDueDate: DateOnly due } || due > today || loan.AutopayAttemptedFor == due) return null;
        if (loan.AutopayFromAccountId is not int fromId) return null;
        var amount = LoanQuote.For(loan, today).ScheduledAmount;
        var result = await InTransactionAsync(async () => await PayLoanCoreAsync(loan.UserId, loan.AccountId, fromId, LoanPaymentKind.Scheduled, null, autopay: true, today));

        // Record the attempt for this due date (its own save: a failed payment was rolled back above).
        _db.ChangeTracker.Clear();
        var tracked = await _db.Loans.FirstAsync(l => l.Id == loanId);
        tracked.AutopayAttemptedFor = due;
        var name = $"{loan.Account!.DisplayName} ••{loan.Account.Last4}";
        if (result.Success)
            _alerts.Add(loan.UserId, AlertType.LoanUpdate, "Loan autopay made", $"Autopay paid {amount:C} toward your {name} ({due:MMM d} payment).", $"/accounts/{loan.AccountId}");
        else
            _alerts.Add(loan.UserId, AlertType.BillFailed, "Loan autopay failed",
                $"Autopay couldn't make the {due:MMM d} payment on your {name}: {result.Message} Please pay {amount:C} by {due.AddDays((await GetSettingsAsync()).LoanGraceDays):MMM d} to avoid a late fee.",
                $"/loans/{loan.AccountId}/pay");
        await _db.SaveChangesAsync();
        return result.Success;
    }

    private async Task<BankResult> LoanLateFeeIfDueAsync(int loanId, DateOnly today, BankSetting settings)
    {
        _db.ChangeTracker.Clear();   // read the loan fresh inside this transaction (not a copy from the autopay step)
        var loan = await _db.Loans.FirstOrDefaultAsync(l => l.Id == loanId);
        if (loan is not { Status: LoanStatus.Active, NextDueDate: DateOnly due }) return new BankResult(false, "nothing due");
        // The next due date that hasn't had a fee yet (a customer can miss more than one).
        var target = loan.LateFeeForDueDate is DateOnly last && last >= due ? last.AddMonths(1) : due;
        if (today <= target.AddDays(settings.LoanGraceDays)) return new BankResult(false, "nothing due");
        // Never for a due date past the end of the schedule.
        var remaining = LoanMath.Schedule(loan.PrincipalRemaining, loan.AprPercent, loan.MonthlyPayment, due, loan.PaymentsMade + 1, loan.TermMonths).Count;
        if (target >= due.AddMonths(remaining)) return new BankResult(false, "nothing due");

        var account = await _db.Accounts.FirstAsync(a => a.Id == loan.AccountId);
        var fee = settings.LoanLateFee;
        account.Balance -= fee;
        loan.FeesOwed += fee;
        loan.LateFeeForDueDate = target;
        var t = AddTransaction(account, TransactionType.LateFee, fee, "Late fee",
                               memo: $"The {target:MMM d} payment was more than {settings.LoanGraceDays} days late");
        if (-account.Balance != loan.PrincipalRemaining + loan.FeesOwed)
            throw new InvalidOperationException($"Loan {loan.Id}: balance {account.Balance} doesn't match principal {loan.PrincipalRemaining} + fees {loan.FeesOwed}.");
        await _db.SaveChangesAsync();
        _alerts.Add(loan.UserId, AlertType.LoanLateFee, "Loan payment overdue",
            $"The {target:MMM d} payment on your {account.DisplayName} ••{account.Last4} is overdue, so a {fee:C} late fee was added. Please make a payment now.",
            $"/loans/{account.Id}/pay");
        _audit.Add("Loan.LateFee", $"Late fee {fee:C} on loan ••{account.Last4} ({target:yyyy-MM-dd} payment overdue)", "Account", account.Id.ToString(),
                   after: new { loanId = loan.Id, fee, dueDate = target, transactionId = t.Id });
        await _db.SaveChangesAsync();
        return new BankResult(true, "fee");
    }

    // Once per due date, 5 days before it, when autopay is off and it isn't paid yet.
    private async Task LoanReminderAsync(Loan loan, DateOnly today)
    {
        if (loan.AutopayEnabled || loan.NextDueDate is not DateOnly due || due < today || due > today.AddDays(5)) return;
        if (due.AddMonths(-1) > today) return;   // already paid ahead
        var name = await _db.Accounts.AsNoTracking().Where(a => a.Id == loan.AccountId).Select(a => a.Nickname ?? "Alto Loan").FirstAsync();
        await _alerts.AddAsync(loan.UserId, AlertType.LoanPaymentDue, "Loan payment due soon",
            $"Your {name} payment of {LoanQuote.For(loan, today).ScheduledAmount:C} is due {due:MMM d}.",
            $"/loans/{loan.AccountId}/pay", dedupeKey: $"loan-due-{loan.Id}-{due:yyyyMMdd}");
        await _db.SaveChangesAsync();
    }
}
