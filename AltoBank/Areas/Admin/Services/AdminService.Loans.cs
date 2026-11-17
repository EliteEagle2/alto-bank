using AltoBank.Areas.Admin.ViewModels;
using AltoBank.Data;
using AltoBank.Models;
using AltoBank.Services;
using Microsoft.EntityFrameworkCore;

namespace AltoBank.Areas.Admin.Services;

public partial interface IAdminService
{
    // ----- Loans. Every change: ExecuteAsync (one transaction + an AuditLog row + a reason). -----
    Task<LoanListViewModel> GetLoansPageAsync(LoanListQuery query, int pageSize = 20);
    Task<LoanDetailViewModel?> GetLoanAsync(int loanAccountId);
    /// <summary>Credits a loan late fee back (FeeWaiver): lowers the fees owed, or the principal if the fee was already paid.</summary>
    Task<AdminResult> WaiveLoanLateFeeAsync(int loanAccountId, int feeTransactionId, string reason);
    /// <summary>Credit = lowers the principal owed; debit = raises it. Keeps the balance equal to principal + fees.</summary>
    Task<AdminResult> PostLoanAdjustmentAsync(int loanAccountId, bool isCredit, decimal amount, string reason);
    /// <summary>A pending loan application: send the customer different terms (a new APR and/or amount) to accept.</summary>
    Task<AdminResult> CounterLoanOfferAsync(int applicationId, decimal? aprPercent, decimal? amount, string reason);
}

public partial class AdminService
{
    public async Task<LoanListViewModel> GetLoansPageAsync(LoanListQuery query, int pageSize = 20)
    {
        var s = await _db.GetSettingsAsync();
        var today = BankTime.Today;
        var q = _db.Loans.AsNoTracking().Include(l => l.Account).ThenInclude(a => a!.User).Include(l => l.Payments).AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.Customer))
        {
            var like = $"%{query.Customer.Trim()}%";
            q = q.Where(l => EF.Functions.Like(l.Account!.User!.FirstName + " " + l.Account.User.LastName, like) || EF.Functions.Like(l.Account!.User!.Email!, like));
        }
        // Overdue depends on today's date and the grace days, so the (small) list is filtered in memory.
        var all = (await q.ToListAsync()).Select(l => LoanService.Build(l, l.Account!, s, today)).ToList();
        var counts = new Dictionary<string, int>
        {
            ["active"] = all.Count(v => v.Loan.Status == LoanStatus.Active),
            ["overdue"] = all.Count(v => v.Overdue),
            ["paidoff"] = all.Count(v => v.Loan.Status == LoanStatus.PaidOff),
        };
        var filtered = (query.Status?.ToLowerInvariant() switch
        {
            "active" => all.Where(v => v.Loan.Status == LoanStatus.Active),
            "overdue" => all.Where(v => v.Overdue),
            "paidoff" => all.Where(v => v.Loan.Status == LoanStatus.PaidOff),
            _ => all
        }).OrderByDescending(v => v.Overdue).ThenBy(v => v.Loan.Status).ThenBy(v => v.Loan.NextDueDate).ThenBy(v => v.Loan.Id).ToList();
        var page = Math.Clamp(query.Page, 1, Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)pageSize)));
        return new LoanListViewModel
        {
            Query = query, Counts = counts,
            Loans = new PagedList<LoanView> { Items = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList(), Page = page, PageSize = pageSize, TotalCount = filtered.Count },
            PendingApplications = await _db.AccountApplications.CountAsync(a => a.AccountType == AccountType.Loan && a.Status == ApplicationStatus.Pending),
            OpenOffers = await _db.AccountApplications.CountAsync(a => a.AccountType == AccountType.Loan && a.Status == ApplicationStatus.Offered)
        };
    }

    public async Task<LoanDetailViewModel?> GetLoanAsync(int loanAccountId)
    {
        var loan = await _db.Loans.AsNoTracking().Include(l => l.Account).ThenInclude(a => a!.User).Include(l => l.Payments)
            .FirstOrDefaultAsync(l => l.AccountId == loanAccountId);
        if (loan == null) return null;
        var view = LoanService.Build(loan, loan.Account!, await _db.GetSettingsAsync(), BankTime.Today);
        return new LoanDetailViewModel
        {
            View = view, Owner = loan.Account!.User!,
            Application = loan.ApplicationId is int appId ? await _db.AccountApplications.AsNoTracking().FirstOrDefaultAsync(a => a.Id == appId) : null,
            DisbursedTo = loan.DisbursedToAccountId is int to ? await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == to) : null,
            AutopayFrom = loan.AutopayFromAccountId is int f ? await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == f) : null,
            LateFees = await _db.Transactions.AsNoTracking().Where(t => t.AccountId == loanAccountId && t.Type == TransactionType.LateFee)
                .OrderByDescending(t => t.Date).ToListAsync(),
            Recent = await _db.Transactions.AsNoTracking().Where(t => t.AccountId == loanAccountId)
                .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id).Take(25).ToListAsync()
        };
    }

    private async Task<(Loan? Loan, Account? Account, AdminResult? Error)> ActiveLoanAsync(int loanAccountId)
    {
        var loan = await _db.Loans.FirstOrDefaultAsync(l => l.AccountId == loanAccountId);
        var account = loan == null ? null : await _db.Accounts.FirstOrDefaultAsync(a => a.Id == loanAccountId);
        if (loan == null || account == null) return (null, null, Fail("Loan not found."));
        if (loan.Status == LoanStatus.PaidOff || account.Status == AccountStatus.Closed) return (null, null, Fail("This loan is paid off."));
        return (loan, account, null);
    }

    // The loan's balance must always equal -(principal + fees); a change that breaks that is a bug, so roll it back.
    private static void CheckLoanBalance(Loan loan, Account account)
    {
        if (-account.Balance != loan.PrincipalRemaining + loan.FeesOwed)
            throw new InvalidOperationException($"Loan {loan.Id}: balance {account.Balance} doesn't match principal {loan.PrincipalRemaining} + fees {loan.FeesOwed}.");
    }

    public Task<AdminResult> WaiveLoanLateFeeAsync(int loanAccountId, int feeTransactionId, string reason) =>
        ExecuteAsync(reason, true, async () =>
        {
            var (loan, account, error) = await ActiveLoanAsync(loanAccountId);
            if (error != null) return error;
            var fee = await _db.Transactions.FirstOrDefaultAsync(t => t.Id == feeTransactionId && t.AccountId == loanAccountId && t.Type == TransactionType.LateFee);
            if (fee == null) return Fail("Late fee not found on this loan.");
            if (fee.Status != TransactionStatus.Posted || fee.ReversedByTransactionId != null) return Fail("That late fee was already waived.");

            // If the customer hasn't paid the fee yet it comes off the fees owed; if they have, the credit lowers the principal.
            var offFees = Math.Min(fee.Amount, loan!.FeesOwed);
            var offPrincipal = Math.Min(fee.Amount - offFees, loan.PrincipalRemaining);
            var credit = offFees + offPrincipal;
            if (credit <= 0) return Fail("There's nothing left on this loan to credit the fee against.");
            account!.Balance += credit;
            loan.FeesOwed -= offFees;
            loan.PrincipalRemaining -= offPrincipal;
            var w = AddTransaction(account, TransactionType.FeeWaiver, credit, "Late fee waived", memo: CustomerMemo);
            w.Category = fee.Category;
            w.ReversalOfTransactionId = fee.Id;   // linked like a reversal, so the same fee can't be waived twice
            fee.Status = TransactionStatus.Reversed;
            await _db.SaveChangesAsync();
            fee.ReversedByTransactionId = w.Id;
            CheckLoanBalance(loan, account);
            _alerts.Add(account.UserId, AlertType.LoanLateFee, "Late fee waived",
                $"We waived the {fee.Amount:C} late fee on your {account.DisplayName} ••{account.Last4}.", $"/accounts/{account.Id}");
            _audit.Add("Loan.WaiveFee", $"Waived the {fee.Amount:C} late fee on loan ••{account.Last4}", "Account", account.Id.ToString(),
                       new { feeTransactionId = fee.Id, feesOwed = loan.FeesOwed + offFees }, new { waived = credit, offFees, offPrincipal, feesOwed = loan.FeesOwed }, reason);
            return Ok($"Waived the {fee.Amount:C} late fee.");
        });

    public Task<AdminResult> PostLoanAdjustmentAsync(int loanAccountId, bool isCredit, decimal amount, string reason) =>
        ExecuteAsync(reason, true, async () =>
        {
            if (BankService.CheckAmount(amount) is { } invalid) return Fail(invalid.Message);
            var (loan, account, error) = await ActiveLoanAsync(loanAccountId);
            if (error != null) return error;
            if (isCredit && amount > loan!.PrincipalRemaining)
                return Fail($"A credit can't be more than the principal still owed ({loan.PrincipalRemaining:C}).");
            var before = new { principal = loan!.PrincipalRemaining, balance = account!.Balance };
            account.Balance += isCredit ? amount : -amount;
            loan.PrincipalRemaining += isCredit ? -amount : amount;
            var tx = AddTransaction(account, isCredit ? TransactionType.AdjustmentCredit : TransactionType.AdjustmentDebit, amount,
                                    isCredit ? "Adjustment: principal lowered" : "Adjustment: principal raised", memo: CustomerMemo);
            await _db.SaveChangesAsync();
            if (loan.PrincipalRemaining == 0m && loan.FeesOwed == 0m)
            {
                loan.Status = LoanStatus.PaidOff;
                loan.PaidOffAt = DateTime.UtcNow;
                loan.NextDueDate = null;
                loan.AutopayEnabled = false;
                SetStatus(account, AccountStatus.Closed, "Paid in full");
                account.ClosedAt = DateTime.UtcNow;
            }
            CheckLoanBalance(loan, account);
            Notify(account, AlertType.LoanUpdate, "Loan adjusted",
                $"We {(isCredit ? "lowered" : "raised")} the principal on your {account.DisplayName} ••{account.Last4} by {amount:C}. You now owe {loan.TotalOwed:C}.");
            _audit.Add("Loan.Adjust", $"{(isCredit ? "Lowered" : "Raised")} principal on loan ••{account.Last4} by {amount:C}", "Account", account.Id.ToString(),
                       before, new { principal = loan.PrincipalRemaining, balance = account.Balance, transactionId = tx.Id, status = loan.Status.ToString() }, reason);
            return Ok($"{(isCredit ? "Lowered" : "Raised")} the principal by {amount:C}. Now owed: {loan.TotalOwed:C}." + (loan.Status == LoanStatus.PaidOff ? " The loan is paid off." : ""));
        });

    public Task<AdminResult> CounterLoanOfferAsync(int applicationId, decimal? aprPercent, decimal? amount, string reason) =>
        ExecuteAsync(reason, true, async () =>
        {
            var app = await _db.AccountApplications.FirstOrDefaultAsync(a => a.Id == applicationId);
            if (app is not { AccountType: AccountType.Loan }) return Fail("Loan application not found.");
            if (app.Status != ApplicationStatus.Pending) return Fail($"Only pending applications can get a counter-offer (this one is {app.Status}).");
            if (aprPercent == null && amount == null) return Fail("Enter a new APR, a new amount, or both.");
            var (min, max, _, _) = LoanMath.Limits(app.LoanKind!.Value);
            if (amount is decimal a && (a < min || a > max || decimal.Round(a, 0) != a))
                return Fail($"The amount must be whole dollars from {min:C0} to {max:C0}.");
            if (aprPercent is decimal r && (r < LoanDecisionRules.MinApr || r > 36m || decimal.Round(r, 2) != r))
                return Fail($"The APR must be between {LoanDecisionRules.MinApr:0.00}% and 36.00%.");
            if ((amount ?? app.LoanAmount) == app.LoanAmount && (aprPercent ?? app.LoanAprPercent) == app.LoanAprPercent)
                return Fail("That's the same as the current terms. Use Approve as offered instead.");
            app.RequestedLoanAmount ??= app.LoanAmount;
            app.LoanAmount = amount ?? app.LoanAmount;
            app.LoanAprPercent = aprPercent ?? app.LoanAprPercent;
            return await OfferLoanAsync(app, counter: true, reason);
        });

    /// <summary>Pending → Offered: the customer is told and can accept on /loans/offer/{id}. Used by Approve and by a counter-offer.</summary>
    private Task<AdminResult> OfferLoanAsync(AccountApplication app, bool counter, string reason)
    {
        app.LoanMonthlyPayment = LoanMath.MonthlyPayment(app.LoanAmount!.Value, app.LoanAprPercent!.Value, app.LoanTermMonths!.Value);
        app.IsCounterOffer = counter;
        app.Status = ApplicationStatus.Offered;
        app.OfferedAt = app.ReviewedAt = DateTime.UtcNow;
        app.ReviewedBy = AdminEmail;
        _alerts.Add(app.UserId, AlertType.LoanUpdate, counter ? "We have a loan offer for you" : "Your loan offer is ready",
            (counter ? $"We can offer {app.LoanAmount:C} at {app.LoanAprPercent:0.00}% APR" : $"You're approved for {app.LoanAmount:C} at {app.LoanAprPercent:0.00}% APR")
            + $": {app.LoanTermMonths} payments of {app.LoanMonthlyPayment:C}. Review and accept it within {BankService.LoanOfferValidDays} days.",
            $"/loans/offer/{app.Id}");
        _audit.Add(counter ? "Application.CounterOffer" : "Application.Approve",
            $"{(counter ? "Counter-offered" : "Approved")} loan application #{app.Id}: {app.LoanAmount:C} at {app.LoanAprPercent:0.00}% for {app.LoanTermMonths} months",
            "Application", app.Id.ToString(), new { status = "Pending", requested = app.RequestedLoanAmount ?? app.LoanAmount },
            new { status = "Offered", amount = app.LoanAmount, apr = app.LoanAprPercent, monthly = app.LoanMonthlyPayment, counter }, reason);
        return Task.FromResult(Ok(counter ? $"Counter-offer sent: {app.LoanAmount:C} at {app.LoanAprPercent:0.00}%. The customer can accept it." : $"Application #{app.Id} approved. The customer can now accept the offer."));
    }
}
