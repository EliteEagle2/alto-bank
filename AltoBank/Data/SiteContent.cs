using AltoBank.Models;

namespace AltoBank.Data;

/// <summary>
/// Static content for the public pages. Kept in code because it never changes at runtime;
/// in a real bank this would come from a CMS or a database table.
/// All of it is fictional (Alto Bank is a class project).
/// </summary>
public static class SiteContent
{
    public static readonly List<JobOpening> Jobs = new()
    {
        new("Senior Backend Engineer (.NET)", "Engineering", "Remote (US)", "Full-time"),
        new("Data Engineer", "Engineering", "Denver, CO · Hybrid", "Full-time"),
        new("Product Designer", "Design", "Denver, CO · Hybrid", "Full-time"),
        new("Fraud Analyst", "Risk", "Remote (US)", "Full-time"),
        new("Compliance Associate", "Legal & Compliance", "Denver, CO", "Full-time"),
        new("Customer Experience Specialist", "Support", "Remote (US)", "Full-time"),
        new("Software Engineering Intern", "Engineering", "Remote (US)", "Summer internship"),
    };

    public static readonly List<NewsArticle> News = new()
    {
        new("summit-savings-410-apy", new DateTime(2026, 10, 14), "Product",
            "Alto Bank raises Summit Savings to 4.10% APY",
            "Every Summit Savings customer now earns 4.10% APY, with no minimum balance and no tiers.",
            new[]
            {
                "Alto Bank today raised the annual percentage yield on Summit Savings to 4.10% for every customer. The new rate applies automatically. There's nothing to sign up for and no balance tier to reach.",
                "\"Savings shouldn't come with fine print,\" said the Alto product team. \"One rate, for everyone, from the first dollar.\"",
                "Customers can open a Summit Savings account from their dashboard in under a minute and move money in from checking instantly."
            }),
        new("peak-card-waitlist", new DateTime(2026, 9, 2), "Product",
            "The Alto Peak card waitlist is now open",
            "Flat 2% cash back, no annual fee, and spending insights inside the Alto dashboard.",
            new[]
            {
                "Alto Bank opened the waitlist for the Alto Peak card, a no-annual-fee credit card that earns a flat 2% cash back on every purchase.",
                "Cardholders will see purchases appear in their Alto dashboard in real time, alongside their checking and savings accounts.",
                "Customers can join the waitlist today. Invitations will roll out in waves."
            }),
        new("instant-transfers", new DateTime(2026, 7, 21), "Feature",
            "Instant transfers between Alto accounts, 24/7",
            "Money now moves between checking and savings the moment you hit Transfer — nights and weekends included.",
            new[]
            {
                "Transfers between a customer's own Alto accounts are now instant, at any hour of any day.",
                "Each transfer is processed as a single all-or-nothing operation, so both accounts update together and balances are always accurate.",
                "The update is live for all customers on web and mobile."
            }),
        new("login-protections", new DateTime(2026, 5, 10), "Security",
            "New login protections for every Alto account",
            "Automatic lockout after repeated failed sign-ins helps stop password-guessing attacks.",
            new[]
            {
                "Alto Bank now temporarily locks an account after five failed sign-in attempts in a row, a simple but effective defense against password guessing.",
                "Passwords at Alto are never stored in plain text; they are salted and hashed so that even Alto employees can't read them.",
                "Customers locked out by mistake can simply wait a few minutes and try again, or contact support."
            }),
        new("alto-turns-two", new DateTime(2026, 3, 3), "Company",
            "Alto Bank turns two",
            "A look back at two years of building banking that feels like it belongs in this decade.",
            new[]
            {
                "Two years ago Alto Bank started with a simple idea: banking should be fast, transparent, and actually pleasant to use.",
                "Since then we've launched no-fee checking, high-yield savings, instant transfers, and a dashboard customers tell us they genuinely enjoy opening.",
                "Thank you to everyone who has banked with us. We're just getting started."
            }),
    };

    public static readonly List<FaqItem> Faqs = new()
    {
        new("Accounts", "How do I open a new account?",
            "Log in and choose Open Account from your dashboard. Pick Checking or Savings, add an optional nickname and opening deposit, and you're done."),
        new("Accounts", "Are there monthly fees or minimum balances?",
            "No. Alto Checking and Summit Savings have no monthly maintenance fees and no minimum balance."),
        new("Accounts", "Where can I see my transaction history?",
            "Select History on any account card. You'll see every transaction with its date, type, amount, and the running balance after it."),
        new("Money movement", "How do I transfer money between my accounts?",
            "Choose Transfer from the dashboard, pick the From and To accounts, and enter an amount. Transfers between your Alto accounts are instant."),
        new("Money movement", "What happens if I try to withdraw more than my balance?",
            "Alto doesn't allow overdrafts. A withdrawal or transfer larger than your available balance is declined, so you'll never pay an overdraft fee."),
        new("Money movement", "Is there a limit on deposits?",
            "Each individual deposit, withdrawal, or transfer can be up to $1,000,000."),
        new("Security", "How is my password protected?",
            "Passwords are salted and hashed before they're stored. Nobody at Alto can see your password."),
        new("Security", "Why was my account locked?",
            "After five failed sign-in attempts in a row we lock the account for a few minutes to stop password guessing. Wait a few minutes and try again."),
        new("Profile", "How do I change my name or email?",
            "Open the menu with your name in the top right and choose Profile. Your email is also your login, so use the new one next time you sign in."),
        new("About Alto", "Is Alto Bank a real bank?",
            "No. Alto Bank is a fictional bank built for an academic project. It holds no real money and is not FDIC insured."),
    };
}
