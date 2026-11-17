using AltoBank.Models;

namespace AltoBank.Services;

/// <summary>What the simulated loan decision knows about the applicant from Alto (no credit bureau).</summary>
public record LoanHistory(int DaysAsCustomer, decimal DepositBalance, bool HasFrozenAccount, bool HasOverdueLoan);

/// <summary>Approved = an OFFER (terms the customer accepts or declines), Pending (staff review, with the terms they'd see) or Denied.</summary>
public record LoanDecision(CreditOutcome Outcome, string Tier, decimal AprPercent, decimal MonthlyPayment, string Note);

/// <summary>
/// LOAN DECISION — SIMULATED. No credit bureau and no SSN: only the income range the applicant chose, the loan they asked
/// for, and their history with Alto. Every page that shows a decision says "Demo decision, no credit check".
///
/// RATE TIER from income: under $25k = D · $25-50k = C · $50-100k = B · $100k+ = A,
///   one tier better for a customer of 60+ days with at least $1,000 in checking and savings.
///   APR = the tier's rate (Admin → Settings), minus the auto-loan discount for an auto loan (never below 1.99%).
/// Then, in order (the first rule that applies decides):
///   1. An Alto loan that's overdue                                 → Denied
///   2. The payment would be over 25% of monthly income            → Denied ("try a smaller amount or a longer term")
///   3. Any frozen Alto account                                     → Pending
///   4. The amount is at or over the review amount (setting)        → Pending ("large loans are reviewed")
///   5. Payment over 15% of monthly income, or tier D               → Pending ("borderline")
///   Otherwise Offered. A pending application keeps the terms it would get, so staff can approve them as offered.
/// </summary>
public static class LoanDecisionRules
{
    public const decimal MinApr = 1.99m;
    public const decimal DenyShare = 0.25m, ReviewShare = 0.15m;

    public static string BaseTier(string? incomeRange) => incomeRange switch
    {
        "Under $25,000" => "D",
        "$25,000 – $49,999" => "C",
        "$50,000 – $99,999" => "B",
        "$100,000 – $199,999" or "$200,000 or more" => "A",
        _ => "D"
    };

    public static string Tier(string? incomeRange, LoanHistory history)
    {
        var tier = BaseTier(incomeRange);
        var established = history.DaysAsCustomer >= 60 && history.DepositBalance >= 1_000m;
        return established && tier != "A" ? ((char)(tier[0] - 1)).ToString() : tier;
    }

    public static decimal TierRate(string tier, BankSetting s) => tier switch
    {
        "A" => s.LoanRateTierA, "B" => s.LoanRateTierB, "C" => s.LoanRateTierC, _ => s.LoanRateTierD
    };

    public static decimal Apr(string tier, LoanKind kind, BankSetting s) =>
        Math.Max(MinApr, TierRate(tier, s) - (kind == LoanKind.Auto ? s.AutoLoanRateDiscount : 0m));

    public static LoanDecision Decide(LoanKind kind, decimal amount, int termMonths, string? incomeRange, LoanHistory history, BankSetting s)
    {
        var tier = Tier(incomeRange, history);
        var apr = Apr(tier, kind, s);
        var payment = LoanMath.MonthlyPayment(amount, apr, termMonths);
        var monthlyIncome = CreditDecisionRules.IncomeEstimate(incomeRange) / 12m;
        var share = monthlyIncome > 0 ? payment / monthlyIncome : 1m;
        LoanDecision D(CreditOutcome o, string note) => new(o, tier, apr, payment, note);

        if (history.HasOverdueLoan)
            return D(CreditOutcome.Denied, "You have an Alto loan with an overdue payment. Bring it up to date before applying for another.");
        if (share > DenyShare)
            return D(CreditOutcome.Denied, $"The {payment:C} monthly payment would be more than a quarter of the monthly income you told us. Try a smaller amount or a longer term.");
        if (history.HasFrozenAccount)
            return D(CreditOutcome.Pending, "One of your accounts is frozen, so a member of our team will review this application.");
        if (amount >= s.LoanReviewAmount)
            return D(CreditOutcome.Pending, $"Loans of {s.LoanReviewAmount:C0} or more are reviewed by a member of our team.");
        if (share > ReviewShare || tier == "D")
            return D(CreditOutcome.Pending, "The payment is a large share of your income, so a member of our team will review this application.");
        return D(CreditOutcome.Approved, $"Tier {tier} rate based on your income and your history with Alto.");
    }
}
