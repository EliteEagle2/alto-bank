using AltoBank.Data;
using AltoBank.Models;
using Microsoft.EntityFrameworkCore;

namespace AltoBank.Services;

// =====================================================================
// SPENDING INSIGHTS + BUDGETS
//   IInsightsService  read-only: one month's numbers (categories, trend, merchants, money in/out, callouts)
//   IBudgetService    budgets (add / change / remove), recategorizing a transaction, budget alerts
// No money moves here, so neither goes through IBankService. Every query is filtered by the signed-in
// user's id; another customer's transaction or budget id is simply "not found" (IDOR protection).
//
// What counts as SPENDING: posted debits in a spending category (everything except Income and Transfers),
// minus refunds (credits) in the same category. Reversed transactions and their reversals are left out, since
// together they cancel. Months are BANK months (America/New_York), like everything else that says "this month".
// =====================================================================

public record CategorySpend(TransactionCategory Category, decimal Amount, decimal PreviousAmount, int Count)
{
    public string Label => Categories.Label(Category);
    public string Color => Categories.Color(Category);
    public string Icon => Categories.Icon(Category);
}

public record MerchantSpend(string Name, TransactionCategory Category, decimal Amount, int Count);

/// <summary>One bar of the month-over-month chart. Spending for the current month is month-to-date.</summary>
public record MonthSpend(DateOnly Month, decimal Spending);

/// <summary>Tone: "up" (spent more), "down" (spent less), "new", "warn" (budget near), "over" (budget over).</summary>
public record Callout(string Tone, string Icon, string Text);

public record BudgetProgress(int BudgetId, TransactionCategory Category, decimal Limit, decimal Spent)
{
    public string Label => Categories.Label(Category);
    public string Icon => Categories.Icon(Category);
    /// <summary>For display only (rounded). The state below uses the exact amounts, so $99.96 of $100 isn't "over".</summary>
    public decimal Percent => Limit <= 0 ? 0 : Math.Round(Spent / Limit * 100m, 1);
    public decimal Remaining => Limit - Spent;
    /// <summary>"ok" under 75%, "near" from 75% (amber), "over" once spending reaches the limit (red).</summary>
    public string State => Spent >= Limit ? "over" : Spent >= Limit * 0.75m ? "near" : "ok";
}

public record InsightsMonth(
    DateOnly Month, DateOnly EarliestMonth, bool IsCurrentMonth,
    int ComparedDays,                // current month: days 1..today compared with the same days last month
    decimal Spending, decimal PreviousSpending,
    decimal MoneyIn, decimal MoneyOut,
    List<CategorySpend> Categories, List<MonthSpend> Trend, List<MerchantSpend> TopMerchants,
    List<Callout> Callouts, List<BudgetProgress> Budgets)
{
    public DateOnly PreviousMonth => Month.AddMonths(-1);
    public bool HasPrevious => Month > EarliestMonth;
    public bool HasNext => !IsCurrentMonth;
}

public interface IInsightsService
{
    /// <summary>Everything the Insights page shows for one bank month (clamped to the months the customer has banked).</summary>
    Task<InsightsMonth> GetMonthAsync(string userId, DateOnly month);
    /// <summary>The month's transactions in one category, newest first (Insights drill-down).</summary>
    Task<List<Transaction>> GetCategoryTransactionsAsync(string userId, DateOnly month, TransactionCategory category);
    /// <summary>Spending per category in one bank month (the Budgets page's "last month you spent" hint).</summary>
    Task<Dictionary<TransactionCategory, decimal>> GetSpendingByCategoryAsync(string userId, DateOnly month);
    /// <summary>First month the customer had an account (bank time).</summary>
    Task<DateOnly> GetEarliestMonthAsync(string userId);
}

public interface IBudgetService
{
    Task<List<BudgetProgress>> GetProgressAsync(string userId, DateOnly month);
    /// <summary>Adds a budget, changes its limit, or brings back a removed one. Audited; may raise a budget alert.</summary>
    Task<BankResult> SaveAsync(string userId, TransactionCategory category, decimal monthlyLimit);
    /// <summary>Soft delete (Status = Removed). Null = not this customer's budget.</summary>
    Task<BankResult?> RemoveAsync(string userId, int budgetId);
    /// <summary>
    /// The customer picks a category for one of their transactions (null = go back to the automatic one).
    /// Null result = not this customer's transaction.
    /// </summary>
    Task<(BankResult Result, Transaction Transaction)?> RecategorizeAsync(string userId, int transactionId, TransactionCategory? category);
    /// <summary>
    /// Checks this month's budgets in these categories and adds an alert the first time each one reaches 75% and
    /// 100% (once per budget per month, via the alert's dedupe key). Adds only; the caller saves.
    /// </summary>
    Task QueueAlertsAsync(string userId, IEnumerable<TransactionCategory> categories);
}

/// <summary>Shared spending query + math for both services.</summary>
internal static class Spending
{
    public record Row(int Id, DateTime Date, TransactionType Type, decimal Amount, TransactionCategory Category, string Description, string? CounterpartyName)
    {
        public bool IsCredit => Transaction.IsCreditType(Type);
        public DateOnly Day => BankTime.DateOf(Date);
        /// <summary>What this row adds to its category's spending: a debit adds, a refund (credit) takes away.</summary>
        public decimal Spend => Categories.IsSpending(Category) ? (IsCredit ? -Amount : Amount) : 0m;
    }

    /// <summary>
    /// Posted rows on the customer's accounts in [fromDay, toDay) (bank days). A refund-type row (a reversal, or a dispute's
    /// provisional credit / clawback) points at its original charge and counts only while that original is still Posted:
    ///   • fully reversed: the original is Reversed, so neither row counts (they cancel out);
    ///   • partly refunded (a "Wrong amount" dispute) or a provisional credit: the original counts in full and the refund
    ///     row takes its part back out of the same category.
    /// </summary>
    public static Task<List<Row>> LoadAsync(ApplicationDbContext db, string userId, DateOnly fromDay, DateOnly toDay)
    {
        var start = BankTime.StartOfDayUtc(fromDay);
        var end = BankTime.StartOfDayUtc(toDay);
        return db.Transactions.AsNoTracking()
            .Where(t => t.Account!.UserId == userId && t.Date >= start && t.Date < end
                        && t.Status == TransactionStatus.Posted
                        && (t.ReversalOfTransactionId == null
                            || db.Transactions.Any(o => o.Id == t.ReversalOfTransactionId && o.Status == TransactionStatus.Posted)))
            .Select(t => new Row(t.Id, t.Date, t.Type, t.Amount, t.Category, t.Description, t.CounterpartyName))
            .ToListAsync();
    }

    /// <summary>Spending per category (never below zero: a big refund doesn't make a category "negative spending").</summary>
    public static Dictionary<TransactionCategory, decimal> ByCategory(IEnumerable<Row> rows) =>
        rows.Where(r => Categories.IsSpending(r.Category))
            .GroupBy(r => r.Category)
            .ToDictionary(g => g.Key, g => Math.Max(0m, g.Sum(r => r.Spend)));

    public static DateOnly MonthOf(DateOnly d) => new(d.Year, d.Month, 1);
}

public class InsightsService : IInsightsService
{
    private const int TrendMonths = 6;
    public const int DrillDownLimit = 100;
    private readonly ApplicationDbContext _db;
    private readonly IBudgetService _budgets;

    public InsightsService(ApplicationDbContext db, IBudgetService budgets)
    {
        _db = db;
        _budgets = budgets;
    }

    public async Task<DateOnly> GetEarliestMonthAsync(string userId)
    {
        var opened = await _db.Accounts.AsNoTracking().Where(a => a.UserId == userId)
            .OrderBy(a => a.OpenedAt).Select(a => (DateTime?)a.OpenedAt).FirstOrDefaultAsync();
        var thisMonth = BankTime.FirstOfThisMonth;
        if (opened is not DateTime first) return thisMonth;
        var m = Spending.MonthOf(BankTime.DateOf(DateTime.SpecifyKind(first, DateTimeKind.Utc)));
        return m > thisMonth ? thisMonth : m;
    }

    public async Task<InsightsMonth> GetMonthAsync(string userId, DateOnly month)
    {
        var earliest = await GetEarliestMonthAsync(userId);
        var thisMonth = BankTime.FirstOfThisMonth;
        month = Spending.MonthOf(month);
        if (month > thisMonth) month = thisMonth;
        if (month < earliest) month = earliest;

        var isCurrent = month == thisMonth;
        var prev = month.AddMonths(-1);
        var monthEnd = month.AddMonths(1);
        // The current month is compared with the SAME days of last month (Nov 1-16 vs Oct 1-16), so a half-finished
        // month isn't measured against a whole one. Past months are compared whole.
        var prevDays = DateTime.DaysInMonth(prev.Year, prev.Month);
        var compared = isCurrent ? Math.Min(BankTime.Today.Day, prevDays) : prevDays;
        var prevCompareEnd = isCurrent ? prev.AddDays(compared) : month;   // past months: the WHOLE previous month

        var trendStart = month.AddMonths(-(TrendMonths - 1));
        if (trendStart < earliest) trendStart = earliest;
        var loadFrom = trendStart < prev ? trendStart : prev;
        var rows = await Spending.LoadAsync(_db, userId, loadFrom, monthEnd);

        var inMonth = rows.Where(r => r.Day >= month && r.Day < monthEnd).ToList();
        var inPrev = rows.Where(r => r.Day >= prev && r.Day < prevCompareEnd).ToList();
        var now = Spending.ByCategory(inMonth);
        var before = Spending.ByCategory(inPrev);

        var categories = Categories.Spending
            .Select(c => new CategorySpend(c, now.GetValueOrDefault(c), before.GetValueOrDefault(c),
                                           inMonth.Count(r => r.Category == c && !r.IsCredit)))
            .Where(c => c.Amount > 0 || c.PreviousAmount > 0)
            .OrderByDescending(c => c.Amount)
            .ToList();

        // Money in / out: everything except moving money between your own accounts (same rule as the dashboard chart).
        var external = inMonth.Where(r => r.Type is not (TransactionType.TransferIn or TransactionType.TransferOut)).ToList();

        var trend = new List<MonthSpend>();
        for (var m = trendStart; m <= month; m = m.AddMonths(1))
        {
            var end = m.AddMonths(1);
            trend.Add(new MonthSpend(m, Spending.ByCategory(rows.Where(r => r.Day >= m && r.Day < end)).Values.Sum()));
        }

        var merchants = inMonth
            .Where(r => !r.IsCredit && Categories.IsSpending(r.Category))
            .GroupBy(r => MerchantName(r))
            .Select(g => new MerchantSpend(g.Key, g.OrderByDescending(r => r.Amount).First().Category, g.Sum(r => r.Amount), g.Count()))
            .OrderByDescending(m => m.Amount).ThenBy(m => m.Name)
            .Take(5)
            .ToList();

        var spending = now.Values.Sum();
        var prevSpending = before.Values.Sum();
        var budgets = await _budgets.GetProgressAsync(userId, month);
        var callouts = BuildCallouts(month, isCurrent, earliest, spending, prevSpending, categories, budgets);

        return new InsightsMonth(month, earliest, isCurrent, compared, spending, prevSpending,
            MoneyIn: external.Where(r => r.IsCredit).Sum(r => r.Amount),
            MoneyOut: external.Where(r => !r.IsCredit).Sum(r => r.Amount),
            categories, trend, merchants, callouts, budgets);
    }

    public async Task<Dictionary<TransactionCategory, decimal>> GetSpendingByCategoryAsync(string userId, DateOnly month)
    {
        month = Spending.MonthOf(month);
        return Spending.ByCategory(await Spending.LoadAsync(_db, userId, month, month.AddMonths(1)));
    }

    public async Task<List<Transaction>> GetCategoryTransactionsAsync(string userId, DateOnly month, TransactionCategory category)
    {
        month = Spending.MonthOf(month);
        var start = BankTime.StartOfDayUtc(month);
        var end = BankTime.StartOfDayUtc(month.AddMonths(1));
        // Same rows the totals count (Spending.LoadAsync), so the list adds up to the amount in the legend.
        return await _db.Transactions.AsNoTracking().Include(t => t.Account)
            .Where(t => t.Account!.UserId == userId && t.Category == category && t.Date >= start && t.Date < end
                        && t.Status == TransactionStatus.Posted
                        && (t.ReversalOfTransactionId == null
                            || _db.Transactions.Any(o => o.Id == t.ReversalOfTransactionId && o.Status == TransactionStatus.Posted)))
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
            .Take(DrillDownLimit + 1)   // one extra tells the page there are more
            .ToListAsync();
    }

    /// <summary>"Bill payment - Keystone Electric (demo) - November bill" → "Keystone Electric (demo)".</summary>
    private static string MerchantName(Spending.Row r)
    {
        if (r.Type == TransactionType.BillPayment && !string.IsNullOrWhiteSpace(r.CounterpartyName)) return r.CounterpartyName!;
        return r.Description.Trim();
    }

    // ---------- Callouts: at most four short sentences about what changed ----------

    private static string Phrase(TransactionCategory c) => c switch
    {
        TransactionCategory.FoodAndDining => "dining",
        TransactionCategory.BillsAndUtilities => "bills and utilities",
        TransactionCategory.Other => "other things",
        _ => Categories.Label(c).ToLowerInvariant()
    };

    private static List<Callout> BuildCallouts(DateOnly month, bool isCurrent, DateOnly earliest, decimal spending, decimal prevSpending,
                                               List<CategorySpend> categories, List<BudgetProgress> budgets)
    {
        var list = new List<Callout>();
        var hasPrevious = month > earliest;
        var than = isCurrent ? "than at this point last month" : $"than in {month.AddMonths(-1):MMMM}";

        static int Pct(decimal now, decimal before) => (int)Math.Round((now - before) / before * 100m);

        // Budgets first: they're the ones the customer asked to be told about (this month only).
        if (isCurrent)
        {
            var daysLeft = DateTime.DaysInMonth(month.Year, month.Month) - BankTime.Today.Day;
            foreach (var b in budgets.Where(b => b.State == "over").OrderByDescending(b => b.Percent).Take(1))
                list.Add(new("over", "bi-exclamation-octagon", b.Remaining == 0
                    ? $"You've reached your {b.Label} budget for this month."
                    : $"You're over your {b.Label} budget by {(-b.Remaining):C}."));
            foreach (var b in budgets.Where(b => b.State == "near").OrderByDescending(b => b.Percent).Take(1))
                list.Add(new("warn", "bi-exclamation-triangle",
                    $"You've used {Math.Min(99, Math.Floor(b.Percent))}% of your {b.Label} budget with {daysLeft} day{(daysLeft == 1 ? "" : "s")} left."));
        }

        if (hasPrevious)
        {
            // Big enough to matter: both months have real spending and the change is at least 10% and $15.
            var changes = categories
                .Where(c => c.PreviousAmount >= 10m && c.Amount > 0 && Math.Abs(c.Amount - c.PreviousAmount) >= 15m)
                .Select(c => (c, pct: Pct(c.Amount, c.PreviousAmount)))
                .Where(x => Math.Abs(x.pct) >= 10)
                .ToList();
            if (changes.Where(x => x.pct > 0).OrderByDescending(x => x.c.Amount - x.c.PreviousAmount).FirstOrDefault() is { c: not null } up)
                list.Add(new("up", "bi-arrow-up-right", $"You spent {up.pct}% more on {Phrase(up.c.Category)} {than}."));
            if (changes.Where(x => x.pct < 0).OrderBy(x => x.c.Amount - x.c.PreviousAmount).FirstOrDefault() is { c: not null } down)
                list.Add(new("down", "bi-arrow-down-right", $"You spent {-down.pct}% less on {Phrase(down.c.Category)} {than}."));

            if (categories.Where(c => c.PreviousAmount == 0 && c.Amount >= 20m).OrderByDescending(c => c.Amount).FirstOrDefault() is { } fresh)
                list.Add(new("new", "bi-stars", $"New {(isCurrent ? "this month" : $"in {month:MMMM}")}: {fresh.Amount:C} on {Phrase(fresh.Category)}."));

            if (list.Count < 4 && prevSpending > 0 && spending > 0)
            {
                var pct = Pct(spending, prevSpending);
                list.Add(pct == 0
                    ? new("down", "bi-dash", $"Your total spending is about the same {than}.")
                    : new(pct > 0 ? "up" : "down", pct > 0 ? "bi-graph-up-arrow" : "bi-graph-down-arrow",
                        $"In total you spent {Math.Abs(pct)}% {(pct > 0 ? "more" : "less")} {than}."));
            }
        }
        return list.Take(4).ToList();
    }
}

public class BudgetService : IBudgetService
{
    public const decimal MaxLimit = 100_000m;
    private readonly ApplicationDbContext _db;
    private readonly IAlertService _alerts;
    private readonly IAuditLogger _audit;

    public BudgetService(ApplicationDbContext db, IAlertService alerts, IAuditLogger audit)
    {
        _db = db;
        _alerts = alerts;
        _audit = audit;
    }

    public async Task<List<BudgetProgress>> GetProgressAsync(string userId, DateOnly month)
    {
        month = Spending.MonthOf(month);
        var budgets = await _db.Budgets.AsNoTracking()
            .Where(b => b.UserId == userId && b.Status == BudgetStatus.Active).ToListAsync();
        if (budgets.Count == 0) return new();
        var spent = Spending.ByCategory(await Spending.LoadAsync(_db, userId, month, month.AddMonths(1)));
        return budgets
            .Select(b => new BudgetProgress(b.Id, b.Category, b.MonthlyLimit, spent.GetValueOrDefault(b.Category)))
            .OrderByDescending(b => b.Percent).ThenBy(b => b.Label)
            .ToList();
    }

    public async Task<BankResult> SaveAsync(string userId, TransactionCategory category, decimal monthlyLimit)
    {
        if (!Enum.IsDefined(category)) return new(false, "Choose a category.");
        if (!Categories.IsSpending(category))
            return new(false, $"{Categories.Label(category)} isn't spending, so it can't have a budget.");
        if (monthlyLimit < 1m || monthlyLimit > MaxLimit)
            return new(false, $"Enter a monthly limit between $1.00 and {MaxLimit:C0}.");
        if (decimal.Round(monthlyLimit, 2) != monthlyLimit)
            return new(false, "Amounts can have at most 2 decimal places.");

        var budget = await _db.Budgets.FirstOrDefaultAsync(b => b.UserId == userId && b.Category == category);
        var label = Categories.Label(category);
        string message;
        if (budget == null)
        {
            budget = new Budget { UserId = userId, Category = category, MonthlyLimit = monthlyLimit };
            _db.Budgets.Add(budget);
            message = $"{label} budget set to {monthlyLimit:C} a month.";
        }
        else
        {
            var wasRemoved = budget.Status == BudgetStatus.Removed;
            var beforeLimit = budget.MonthlyLimit;
            budget.MonthlyLimit = monthlyLimit;
            budget.Status = BudgetStatus.Active;
            budget.UpdatedAt = DateTime.UtcNow;
            message = wasRemoved ? $"{label} budget set to {monthlyLimit:C} a month."
                                 : $"{label} budget changed from {beforeLimit:C} to {monthlyLimit:C} a month.";
        }
        // The budget and its audit row commit together (one DB transaction), so a change is never saved unaudited.
        try
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            await _db.SaveChangesAsync();
            _audit.Add("Budget.Save", message, "Budget", budget.Id.ToString(), after: new { category = category.ToString(), monthlyLimit });
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (DbUpdateException)
        {
            // Two "Add budget" requests at once (double-click): the unique (customer, category) index kept one.
            _db.ChangeTracker.Clear();
            return new(false, $"You already have a {label} budget. Use Change or remove to adjust it.");
        }
        await AlertSafelyAsync(userId, category);   // a new, lower limit may already be reached
        return new(true, message);
    }

    public async Task<BankResult?> RemoveAsync(string userId, int budgetId)
    {
        var budget = await _db.Budgets.FirstOrDefaultAsync(b => b.Id == budgetId && b.UserId == userId);
        if (budget == null) return null;
        if (budget.Status == BudgetStatus.Removed) return new(false, "That budget was already removed.");
        budget.Status = BudgetStatus.Removed;
        budget.UpdatedAt = DateTime.UtcNow;
        var label = Categories.Label(budget.Category);
        _audit.Add("Budget.Remove", $"Removed the {label} budget ({budget.MonthlyLimit:C} a month)", "Budget", budget.Id.ToString(),
                   before: new { status = "Active" }, after: new { status = "Removed" });
        await _db.SaveChangesAsync();
        return new(true, $"{label} budget removed. Your spending history is unchanged.");
    }

    public async Task<(BankResult Result, Transaction Transaction)?> RecategorizeAsync(string userId, int transactionId, TransactionCategory? category)
    {
        var t = await _db.Transactions.Include(x => x.Account)
            .FirstOrDefaultAsync(x => x.Id == transactionId && x.Account!.UserId == userId);   // ownership check
        if (t == null) return null;
        if (category is TransactionCategory c && !Enum.IsDefined(c)) return (new BankResult(false, "Choose a category."), t);

        var before = t.Category;
        var wasOverridden = t.CategoryOverridden;
        if (category is TransactionCategory chosen)
        {
            t.Category = chosen;
            t.CategoryOverridden = true;
        }
        else
        {
            t.Category = CategoryRules.For(t.Type, t.Description);   // back to automatic
            t.CategoryOverridden = false;
        }
        if (t.Category == before && t.CategoryOverridden == wasOverridden)
            return (new BankResult(true, $"Already in {Categories.Label(t.Category)}."), t);

        _audit.Add("Transaction.Recategorize",
            $"Moved transaction #{t.Id} from {Categories.Label(before)} to {Categories.Label(t.Category)}{(t.CategoryOverridden ? "" : " (automatic)")}",
            "Transaction", t.Id.ToString(),
            before: new { category = before.ToString(), overridden = wasOverridden },
            after: new { category = t.Category.ToString(), overridden = t.CategoryOverridden });
        await _db.SaveChangesAsync();   // the change and its audit row together
        if (BankTime.DateOf(DateTime.SpecifyKind(t.Date, DateTimeKind.Utc)) >= BankTime.FirstOfThisMonth)
            await AlertSafelyAsync(userId, t.Category);
        return (new BankResult(true, $"Moved to {Categories.Label(t.Category)}."), t);
    }

    /// <summary>Budget alerts after a change that is already saved: a problem only loses the alert, never the change.</summary>
    private async Task AlertSafelyAsync(string userId, TransactionCategory category)
    {
        try
        {
            await QueueAlertsAsync(userId, new[] { category });
            await _db.SaveChangesAsync();
        }
        catch (Exception)
        {
            _db.ChangeTracker.Clear();   // e.g. the same alert was added by a parallel request
        }
    }

    public async Task QueueAlertsAsync(string userId, IEnumerable<TransactionCategory> categories)
    {
        var wanted = categories.Where(Categories.IsSpending).Distinct().ToList();
        if (wanted.Count == 0) return;
        var month = BankTime.FirstOfThisMonth;
        var budgets = await _db.Budgets.AsNoTracking()
            .Where(b => b.UserId == userId && b.Status == BudgetStatus.Active && wanted.Contains(b.Category)).ToListAsync();
        if (budgets.Count == 0) return;

        var spent = Spending.ByCategory(await Spending.LoadAsync(_db, userId, month, month.AddMonths(1)));
        foreach (var b in budgets)
        {
            var p = new BudgetProgress(b.Id, b.Category, b.MonthlyLimit, spent.GetValueOrDefault(b.Category));
            var key = $"budget:{b.Id}:{month:yyyy-MM}:";
            if (p.State == "over")
                await _alerts.AddAsync(userId, AlertType.BudgetNearLimit, $"{p.Label} budget reached",
                    p.Remaining == 0
                        ? $"You've spent {p.Spent:C} on {p.Label} this month, your whole {p.Limit:C} budget."
                        : $"You've spent {p.Spent:C} on {p.Label} this month, {(-p.Remaining):C} over your {p.Limit:C} budget.",
                    "/budgets", key + "100");
            else if (p.State == "near")
                await _alerts.AddAsync(userId, AlertType.BudgetNearLimit, $"{p.Label} budget at {Math.Min(99, Math.Floor(p.Percent))}%",
                    $"You've spent {p.Spent:C} of your {p.Limit:C} {p.Label} budget. {p.Remaining:C} left this month.", "/budgets", key + "75");
        }
    }
}
