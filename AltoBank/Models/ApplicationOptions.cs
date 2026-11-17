namespace AltoBank.Models;

/// <summary>
/// The fixed choices offered on the account application. The server checks every
/// posted value against these lists, so a tampered form can't save anything else.
/// </summary>
public static class ApplicationOptions
{
    public static readonly Dictionary<string, string> Citizenship = new()
    {
        ["US"] = "U.S. citizen",
        ["PR"] = "Permanent resident",
        ["Other"] = "Other",
    };

    public static readonly string[] EmploymentStatuses =
        { "Employed", "Self-employed", "Student", "Retired", "Unemployed" };

    public static readonly string[] IncomeRanges =
    {
        "Under $25,000", "$25,000 – $49,999", "$50,000 – $99,999",
        "$100,000 – $199,999", "$200,000 or more"
    };

    public static readonly string[] FundSources = { "Employment", "Savings", "Gift", "Other" };

    // Loans: what the money is for.
    public static readonly string[] PersonalLoanPurposes =
        { "Debt consolidation", "Home improvement", "Major purchase", "Medical expenses", "Moving", "Something else" };
    public static readonly string[] AutoLoanPurposes = { "New vehicle", "Used vehicle", "Refinance my current auto loan" };
    public static string[] LoanPurposes(LoanKind kind) => kind == LoanKind.Auto ? AutoLoanPurposes : PersonalLoanPurposes;

    // 50 states + DC, code -> name.
    public static readonly SortedDictionary<string, string> States = new()
    {
        ["AL"] = "Alabama", ["AK"] = "Alaska", ["AZ"] = "Arizona", ["AR"] = "Arkansas", ["CA"] = "California",
        ["CO"] = "Colorado", ["CT"] = "Connecticut", ["DE"] = "Delaware", ["DC"] = "District of Columbia",
        ["FL"] = "Florida", ["GA"] = "Georgia", ["HI"] = "Hawaii", ["ID"] = "Idaho", ["IL"] = "Illinois",
        ["IN"] = "Indiana", ["IA"] = "Iowa", ["KS"] = "Kansas", ["KY"] = "Kentucky", ["LA"] = "Louisiana",
        ["ME"] = "Maine", ["MD"] = "Maryland", ["MA"] = "Massachusetts", ["MI"] = "Michigan", ["MN"] = "Minnesota",
        ["MS"] = "Mississippi", ["MO"] = "Missouri", ["MT"] = "Montana", ["NE"] = "Nebraska", ["NV"] = "Nevada",
        ["NH"] = "New Hampshire", ["NJ"] = "New Jersey", ["NM"] = "New Mexico", ["NY"] = "New York",
        ["NC"] = "North Carolina", ["ND"] = "North Dakota", ["OH"] = "Ohio", ["OK"] = "Oklahoma", ["OR"] = "Oregon",
        ["PA"] = "Pennsylvania", ["RI"] = "Rhode Island", ["SC"] = "South Carolina", ["SD"] = "South Dakota",
        ["TN"] = "Tennessee", ["TX"] = "Texas", ["UT"] = "Utah", ["VT"] = "Vermont", ["VA"] = "Virginia",
        ["WA"] = "Washington", ["WV"] = "West Virginia", ["WI"] = "Wisconsin", ["WY"] = "Wyoming",
    };
}
