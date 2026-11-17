using AltoBank.Models;

namespace AltoBank.Services;

public enum CreditOutcome { Approved, Pending, Denied }

/// <summary>The simulated decision: approved with a limit, pending (with the limit staff would see), or denied with a reason.</summary>
public record CreditDecision(CreditOutcome Outcome, decimal? CreditLimit, string Note);

/// <summary>What we know about the applicant from Alto (no credit bureau).</summary>
public record CreditHistory(int DaysAsCustomer, decimal DepositBalance, bool HasFrozenAccount, bool HasOpenCreditCard);

/// <summary>
/// ALTO PEAK CREDIT DECISION — SIMULATED. Nothing is pulled from a credit bureau and no SSN is used: the decision comes
/// only from the income range the applicant chose, their monthly housing payment, and their history with Alto. Every page
/// that shows a decision says "Demo decision, no credit check".
///
/// The rules, in order (the first one that applies decides):
///   1. Already has an open Alto Peak card                      → Denied  ("one Peak card per customer")
///   2. Any frozen Alto account                                 → Pending (staff look at it)
///   3. Housing costs more than 50% of income (yearly)          → Denied  ("housing payment is high for the income")
///   4. New to Alto (no deposit accounts yet)                   → Pending
///   5. Housing between 40% and 50% of income                   → Pending
///   Otherwise approved with a limit:
///      base by income: under $25k $500 · $25-50k $1,500 · $50-100k $3,000 · $100-200k $4,000 · $200k+ $4,500
///      + $500 if a customer for 60+ days with at least $1,000 in checking and savings, capped at $5,000.
///   A pending application carries the limit it would get, so an admin approving it opens the card with that limit.
/// </summary>
public static class CreditDecisionRules
{
    public const decimal MinLimit = 500m, MaxLimit = 5000m;

    /// <summary>A yearly income for each range (the middle of it), used only for the housing-cost check.</summary>
    public static decimal IncomeEstimate(string? incomeRange) => incomeRange switch
    {
        "Under $25,000" => 18_000m,
        "$25,000 – $49,999" => 37_500m,
        "$50,000 – $99,999" => 75_000m,
        "$100,000 – $199,999" => 150_000m,
        "$200,000 or more" => 250_000m,
        _ => 0m
    };

    public static decimal BaseLimit(string? incomeRange) => incomeRange switch
    {
        "Under $25,000" => 500m,
        "$25,000 – $49,999" => 1_500m,
        "$50,000 – $99,999" => 3_000m,
        "$100,000 – $199,999" => 4_000m,
        "$200,000 or more" => 4_500m,
        _ => MinLimit
    };

    public static CreditDecision Decide(string? incomeRange, decimal monthlyHousing, CreditHistory history)
    {
        var income = IncomeEstimate(incomeRange);
        var housingShare = income > 0 ? monthlyHousing * 12m / income : 1m;
        var limit = BaseLimit(incomeRange);
        if (history.DaysAsCustomer >= 60 && history.DepositBalance >= 1_000m) limit += 500m;
        limit = Math.Clamp(limit, MinLimit, MaxLimit);

        if (history.HasOpenCreditCard)
            return new(CreditOutcome.Denied, null, "You already have an Alto Peak card. Each customer can have one.");
        if (history.HasFrozenAccount)
            return new(CreditOutcome.Pending, limit, "One of your accounts is frozen, so a member of our team will review this application.");
        if (housingShare > 0.50m)
            return new(CreditOutcome.Denied, null, "Your monthly housing payment is high compared to the income you told us, so we can't offer a card right now.");
        if (history.DaysAsCustomer == 0 || history.DepositBalance <= 0)
            return new(CreditOutcome.Pending, limit, "You're new to Alto, so a member of our team will review this application.");
        if (housingShare > 0.40m)
            return new(CreditOutcome.Pending, limit, "Your housing payment is a large share of your income, so a member of our team will review this application.");
        return new(CreditOutcome.Approved, limit, $"Approved with a {limit:C0} credit limit based on your income and your history with Alto.");
    }
}
