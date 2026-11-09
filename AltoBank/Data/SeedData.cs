using AltoBank.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AltoBank.Data;

/// <summary>
/// SEEDER: runs at startup (Program.cs). It applies migrations, then makes sure the demo data exists:
///   • an "Admin" role and an admin user
///   • 3 customers, each with several checking/savings accounts
///   • ~3 months of realistic, varied transactions per account (payroll, rent, bills,
///     groceries, coffee, transfers to savings, monthly interest, ...)
///   • an approved AccountApplication behind every seeded account
/// Every step checks first ("ensure"), so running it again never duplicates data.
/// The random numbers use a fixed seed per customer, so the data is the same every time.
///
/// Logins:  demo@altobank.com / Demo123!   maya@altobank.com / Demo123!
///          sam@altobank.com / Demo123!    admin@altobank.com / Admin123!
/// </summary>
public static class SeedData
{
    public const string AdminRole = "Admin";
    public const string CustomerRole = "Customer";
    public const string DemoEmail = "demo@altobank.com";
    public const string DemoPassword = "Demo123!";
    public const string AdminEmail = "admin@altobank.com";
    public const string AdminPassword = "Admin123!";

    // Accounts open on different days across the last ~11 weeks (so the admin "New accounts per week" chart,
    // which covers 12 weeks, has data). History starts on each account's own open date.
    private const int HistoryDays = 76;

    // ---------- Who we seed ----------
    private record SeedAccount(string Number, AccountType Type, string Nickname, decimal OpeningDeposit, int OpenedDaysAgo,
        string? CardDesign = null, string? GoalName = null, decimal? GoalAmount = null);

    private record SeedCustomer(
        string Email, string First, string? Middle, string Last, DateOnly Dob, string Phone,
        string Street, string? Apt, string City, string State, string Zip,
        string Employer, string Occupation, string Income,
        decimal Paycheck, decimal Rent, int RandomSeed, SeedAccount[] Accounts);

    private static readonly SeedCustomer[] Customers =
    {
        new(DemoEmail, "Jordan", "A", "Rivera", new DateOnly(1996, 4, 12), "(814) 555-0142",
            "1200 12th Ave", "Apt 3B", "Altoona", "PA", "16601",
            "Northwind Labs", "Software Developer", "$50,000 – $99,999",
            Paycheck: 1850.75m, Rent: 1200m, RandomSeed: 11,
            Accounts: new SeedAccount[]
            {
                new("4410274821", AccountType.Checking, "Everyday Checking", 2500m, 76, CardDesign: CardDesigns.Aurora),
                new("4410279356", AccountType.Savings, "Summit Savings", 5000m, 70, GoalName: "Emergency Fund", GoalAmount: 10000m),
                new("4410271188", AccountType.Savings, "Vacation Fund", 800m, 30, GoalName: "Trip to Japan", GoalAmount: 4000m),
            }),
        new("maya@altobank.com", "Maya", "L", "Chen", new DateOnly(1988, 9, 30), "(215) 555-0187",
            "1700 Walnut St", "Unit 12", "Philadelphia", "PA", "19103",
            "Liberty Health Partners", "Registered Nurse", "$100,000 – $199,999",
            Paycheck: 2420m, Rent: 1650m, RandomSeed: 22,
            Accounts: new SeedAccount[]
            {
                new("5520831402", AccountType.Checking, "Main Checking", 3200m, 63, CardDesign: CardDesigns.Violet),
                new("5520836617", AccountType.Checking, "Bills", 600m, 55, CardDesign: CardDesigns.Onyx),
                new("5520839920", AccountType.Savings, "House Fund", 12000m, 41, GoalName: "House Down Payment", GoalAmount: 40000m),
            }),
        new("sam@altobank.com", "Samuel", null, "Okafor", new DateOnly(2001, 2, 18), "(717) 555-0119",
            "215 Market St", null, "Harrisburg", "PA", "17101",
            "Keystone Logistics", "Warehouse Supervisor", "$25,000 – $49,999",
            Paycheck: 1580m, Rent: 980m, RandomSeed: 33,
            Accounts: new SeedAccount[]
            {
                new("6630412257", AccountType.Checking, "Everyday Checking", 900m, 48, CardDesign: CardDesigns.Aurora),
                new("6630418834", AccountType.Savings, "Summit Savings", 1500m, 19),
            }),
    };

    // ---------- Entry point ----------
    public static async Task InitializeAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

        // Apply any pending migrations (creates altobank.db on first run).
        await db.Database.MigrateAsync();

        await EnsureAdminAsync(userManager, roleManager);
        if (!await roleManager.RoleExistsAsync(CustomerRole))
            await roleManager.CreateAsync(new IdentityRole(CustomerRole));

        foreach (var customer in Customers)
            await EnsureCustomerAsync(db, userManager, customer);

        // Every non-admin user is a Customer (covers people who registered before roles existed).
        foreach (var user in await userManager.Users.ToListAsync())
        {
            if (!await userManager.IsInRoleAsync(user, AdminRole) && !await userManager.IsInRoleAsync(user, CustomerRole))
                await userManager.AddToRoleAsync(user, CustomerRole);
        }

        await EnsureDebitCardsAsync(db, services.GetRequiredService<AltoBank.Services.ICardService>());
        await EnsureReviewDemoDataAsync(db, userManager);
    }

    /// <summary>
    /// Backfill for older databases: an open checking account that has NEVER had a card gets one.
    /// Accounts whose cards were all cancelled are left alone; the customer requests a new card themselves
    /// (otherwise a restart would quietly issue a second card next to the one they request).
    /// </summary>
    private static async Task EnsureDebitCardsAsync(ApplicationDbContext db, AltoBank.Services.ICardService cards)
    {
        var needCard = await db.Accounts.Include(a => a.User)
            .Where(a => a.AccountType == AccountType.Checking && a.Status != AccountStatus.Closed
                        && !a.Cards.Any())
            .ToListAsync();
        foreach (var account in needCard)
        {
            var card = await cards.IssueAsync(account, $"{account.User!.FirstName} {account.User.LastName}", account.CardDesign);
            card.IssuedAt = account.OpenedAt;
        }
        if (needCard.Count > 0) await db.SaveChangesAsync();
    }

    /// <summary>
    /// Gives the admin portal something to review: Sam has a PENDING savings application,
    /// Maya has a DENIED one, and Jordan has an internal note. Added once (checked by marker).
    /// </summary>
    private static async Task EnsureReviewDemoDataAsync(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        var admin = await userManager.FindByEmailAsync(AdminEmail);
        if (admin != null && admin.CreatedAt == null) { admin.CreatedAt = DateTime.UtcNow.Date.AddDays(-HistoryDays - 30); await userManager.UpdateAsync(admin); }

        async Task AddApplicationOnce(string email, string nickname, AccountType type, ApplicationStatus status,
                                      decimal deposit, int daysAgo, string? denial)
        {
            var c = Customers.First(x => x.Email == email);
            var user = await userManager.FindByEmailAsync(email);
            if (user == null || await db.AccountApplications.AnyAsync(a => a.UserId == user.Id && a.Nickname == nickname)) return;
            var source = deposit > 0
                ? await db.Accounts.Where(a => a.UserId == user.Id && a.AccountType == AccountType.Checking).OrderBy(a => a.Id).FirstOrDefaultAsync()
                : null;
            var when = DateTime.UtcNow.AddDays(-daysAgo).AddHours(-3);
            db.AccountApplications.Add(new AccountApplication
            {
                UserId = user.Id, Status = status, SubmittedAt = when,
                ReviewedAt = status == ApplicationStatus.Pending ? null : when.AddHours(5),
                ReviewedBy = status == ApplicationStatus.Pending ? null : AdminEmail,
                DenialReason = denial,
                AccountType = type, Nickname = nickname, CardDesign = type == AccountType.Checking ? CardDesigns.Violet : null,
                FirstName = c.First, MiddleInitial = c.Middle, LastName = c.Last, DateOfBirth = c.Dob, Citizenship = "US",
                Email = c.Email, Phone = c.Phone, Street = c.Street, Apt = c.Apt, City = c.City, State = c.State, Zip = c.Zip,
                LivedAtAddressTwoYears = true, EmploymentStatus = "Employed", Employer = c.Employer, Occupation = c.Occupation,
                IncomeRange = c.Income, SourceOfFunds = "Employment", SsnLast4 = new Random(c.RandomSeed + 7).Next(0, 10000).ToString("D4"),
                OpeningDeposit = deposit, FundingAccountId = source?.Id,
                FundingSource = source == null ? null : $"{source.DisplayName} ••{source.Last4}",
                ESignName = $"{c.First} {c.Last}", ESignedAt = when
            });
            await db.SaveChangesAsync();
        }

        await AddApplicationOnce("sam@altobank.com", "Emergency Fund", AccountType.Savings, ApplicationStatus.Pending, 200m, 1, null);
        await AddApplicationOnce("maya@altobank.com", "Travel Card", AccountType.Checking, ApplicationStatus.Denied, 0m, 12,
            "You already have two open checking accounts, which is our current limit. You're welcome to apply for savings instead.");

        var jordan = await userManager.FindByEmailAsync(DemoEmail);
        if (jordan != null && admin != null && !await db.CustomerNotes.AnyAsync(n => n.CustomerUserId == jordan.Id))
        {
            db.CustomerNotes.Add(new CustomerNote
            {
                CustomerUserId = jordan.Id, AuthorUserId = admin.Id, AuthorName = "Alex Morgan",
                Text = "Called about how to set up direct deposit. Walked them through the Direct Deposit tab. No further action needed.",
                CreatedAt = DateTime.UtcNow.AddDays(-6)
            });
            await db.SaveChangesAsync();
        }
    }

    /// <summary>One-line-per-table summary printed by "dotnet run -- --seed".</summary>
    public static async Task<string> SummaryAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var admins = await userManager.GetUsersInRoleAsync(AdminRole);
        var customers = await userManager.GetUsersInRoleAsync(CustomerRole);
        var oldest = await db.Transactions.OrderBy(t => t.Date).Select(t => (DateTime?)t.Date).FirstOrDefaultAsync();
        return string.Join(Environment.NewLine,
            "Seed complete:",
            $"  Admins:        {admins.Count}  ({string.Join(", ", admins.Select(a => a.Email))})",
            $"  Customers:     {customers.Count}",
            $"  Accounts:      {await db.Accounts.CountAsync()}",
            $"  Transactions:  {await db.Transactions.CountAsync()}" + (oldest is DateTime d ? $"  (since {d:yyyy-MM-dd})" : ""),
            $"  Applications:  {await db.AccountApplications.CountAsync()}",
            $"  Logins: {DemoEmail} / {DemoPassword}   {AdminEmail} / {AdminPassword}");
    }

    private static async Task EnsureAdminAsync(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
    {
        if (!await roleManager.RoleExistsAsync(AdminRole))
            await roleManager.CreateAsync(new IdentityRole(AdminRole));

        var admin = await userManager.FindByEmailAsync(AdminEmail);
        if (admin == null)
        {
            admin = new ApplicationUser
            {
                UserName = AdminEmail, Email = AdminEmail, EmailConfirmed = true,
                FirstName = "Alex", LastName = "Morgan"
            };
            var result = await userManager.CreateAsync(admin, AdminPassword);
            if (!result.Succeeded) throw new InvalidOperationException("Could not seed admin: " + string.Join(", ", result.Errors.Select(e => e.Description)));
        }
        if (!await userManager.IsInRoleAsync(admin, AdminRole))
            await userManager.AddToRoleAsync(admin, AdminRole);
    }

    private static async Task EnsureCustomerAsync(ApplicationDbContext db, UserManager<ApplicationUser> userManager, SeedCustomer c)
    {
        var user = await userManager.FindByEmailAsync(c.Email);
        var isNew = user == null;
        if (user == null)
        {
            user = new ApplicationUser { UserName = c.Email, Email = c.Email, EmailConfirmed = true };
            ApplyProfile(user, c);
            var result = await userManager.CreateAsync(user, DemoPassword);
            if (!result.Succeeded) throw new InvalidOperationException("Could not seed " + c.Email);
        }
        else if (user.DateOfBirth == null)
        {
            // Older databases: the user exists but has no saved profile yet — fill it in.
            ApplyProfile(user, c);
            await userManager.UpdateAsync(user);
        }

        // Accounts + transactions (only for brand-new seeded users, never on top of real activity).
        if (isNew)
        {
            var accounts = c.Accounts.Select(a => new Account
            {
                UserId = user.Id, AccountNumber = a.Number, AccountType = a.Type, Nickname = a.Nickname,
                CardDesign = a.CardDesign, GoalName = a.GoalName, GoalAmount = a.GoalAmount,
                OpenedAt = DateTime.UtcNow.Date.AddDays(-a.OpenedDaysAgo).AddHours(8)
            }).ToList();
            var transfers = GenerateHistory(c, accounts);
            db.Accounts.AddRange(accounts);
            await db.SaveChangesAsync();
            LinkTransfers(transfers);   // now that the rows have Ids
            await db.SaveChangesAsync();
        }
        if (user.CreatedAt == null)
        {
            user.CreatedAt = DateTime.UtcNow.Date.AddDays(-c.Accounts.Max(a => a.OpenedDaysAgo)).AddHours(8);
            await userManager.UpdateAsync(user);
        }

        // One approved application behind every account the user has (if not already recorded).
        var userAccounts = await db.Accounts.Where(a => a.UserId == user.Id).ToListAsync();
        var appliedIds = await db.AccountApplications.Where(a => a.UserId == user.Id && a.AccountId != null)
                                                     .Select(a => a.AccountId!.Value).ToListAsync();
        var rng = new Random(c.RandomSeed);
        foreach (var account in userAccounts.Where(a => !appliedIds.Contains(a.Id)))
        {
            var opening = c.Accounts.FirstOrDefault(s => s.Number == account.AccountNumber)?.OpeningDeposit ?? 0m;
            db.AccountApplications.Add(new AccountApplication
            {
                UserId = user.Id, AccountId = account.Id, Status = ApplicationStatus.Approved,
                SubmittedAt = account.OpenedAt.AddMinutes(-12),
                ReviewedAt = account.OpenedAt.AddMinutes(-12), ReviewedBy = "Auto-approved",
                CardDesign = account.CardDesign,
                AccountType = account.AccountType, Nickname = account.Nickname,
                FirstName = c.First, MiddleInitial = c.Middle, LastName = c.Last, DateOfBirth = c.Dob, Citizenship = "US",
                Email = c.Email, Phone = c.Phone, Street = c.Street, Apt = c.Apt, City = c.City, State = c.State, Zip = c.Zip,
                LivedAtAddressTwoYears = true,
                EmploymentStatus = "Employed", Employer = c.Employer, Occupation = c.Occupation, IncomeRange = c.Income,
                SourceOfFunds = "Employment, Savings",
                SsnLast4 = rng.Next(0, 10000).ToString("D4"),
                OpeningDeposit = opening, FundingSource = opening > 0 ? "External bank (simulated)" : null,
                ESignName = $"{c.First} {c.Last}", ESignedAt = account.OpenedAt.AddMinutes(-12)
            });
        }
        await db.SaveChangesAsync();
    }

    private static void ApplyProfile(ApplicationUser user, SeedCustomer c)
    {
        user.FirstName = c.First; user.MiddleInitial = c.Middle; user.LastName = c.Last;
        user.DateOfBirth = c.Dob; user.PhoneNumber = c.Phone;
        user.Street = c.Street; user.Apt = c.Apt; user.City = c.City; user.State = c.State; user.Zip = c.Zip;
    }

    // ---------- Transaction generator ----------

    /// <summary>
    /// Walks day by day from the customer's first account opening to yesterday and creates realistic activity.
    /// Each account gets its opening deposit on its own open date, and nothing touches an account before that.
    /// Debits that would overdraw an account are skipped, so balances never go negative.
    /// </summary>
    private static List<(Transaction Out, Transaction In)> GenerateHistory(SeedCustomer c, List<Account> accounts)
    {
        var rng = new Random(c.RandomSeed);
        var transfers = new List<(Transaction Out, Transaction In)>();   // linked after saving (they need Ids)
        var checking = accounts.First(a => a.AccountType == AccountType.Checking);
        var billsChecking = accounts.Where(a => a.AccountType == AccountType.Checking).Skip(1).FirstOrDefault();
        var savings = accounts.Where(a => a.AccountType == AccountType.Savings).ToList();
        var firstOpen = c.Accounts.Max(a => a.OpenedDaysAgo);
        var start = DateTime.UtcNow.Date.AddDays(-firstOpen);

        // Bills come out of the separate "Bills" checking once it exists, otherwise the main checking.
        Account BillsFrom(DateTime d) => billsChecking != null && d.Date >= billsChecking.OpenedAt.Date ? billsChecking : checking;

        decimal Money(double min, double max) => Math.Round((decimal)(min + rng.NextDouble() * (max - min)), 2);

        for (int day = 0; day < firstOpen; day++)   // stops at yesterday, so nothing is future-dated
        {
            var date = start.AddDays(day);
            // Accounts that open today get their opening deposit first.
            foreach (var (account, seed) in accounts.Zip(c.Accounts).Where(p => p.First.OpenedAt.Date == date))
                Post(account, TransactionType.Deposit, seed.OpeningDeposit, "Opening deposit", date.AddHours(8));   // before any other activity that day
            if (day == 0) continue;
            var at = date.AddHours(8);                 // each event this day gets a later timestamp
            DateTime Next() => at = at.AddMinutes(rng.Next(25, 140));

            // Payday every other Friday.
            if (date.DayOfWeek == DayOfWeek.Friday && (day / 7) % 2 == 0)
                Post(checking, TransactionType.Deposit, c.Paycheck + Money(-40, 40), $"Payroll - {c.Employer}", Next());

            switch (date.Day)
            {
                case 1:
                    Post(BillsFrom(date), TransactionType.Withdrawal, c.Rent, "Rent payment", Next());
                    break;
                case 2:
                    // Monthly savings transfers from checking.
                    foreach (var s in savings)
                        Transfer(checking, s, s == savings[0] ? Money(250, 450) : Money(100, 200), Next());
                    if (billsChecking != null) Transfer(checking, billsChecking, c.Rent + 400m, Next());
                    break;
                case 8: Post(BillsFrom(date), TransactionType.Withdrawal, Money(45, 95), "Mobile phone bill", Next()); break;
                case 12: Post(BillsFrom(date), TransactionType.Withdrawal, Money(65, 70), "Internet service", Next()); break;
                case 15: Post(BillsFrom(date), TransactionType.Withdrawal, Money(58, 145), "Electric bill", Next()); break;
                case 21: Post(checking, TransactionType.Withdrawal, 15.99m, "Streamly subscription", Next()); break;
                case 24: Post(checking, TransactionType.Withdrawal, Money(110, 160), "Car insurance", Next()); break;
            }

            // Savings earn interest on the last day of each month (4.10% APY ≈ 0.335%/month).
            if (date.AddDays(1).Day == 1)
                foreach (var s in savings.Where(s => s.Balance > 0))
                    Post(s, TransactionType.Interest, Math.Round(s.Balance * 0.041m / 12m, 2), "Interest earned", date.AddHours(23));

            // Everyday spending.
            if (day % 6 == 0 || rng.NextDouble() < 0.08) Post(checking, TransactionType.Withdrawal, Money(38, 165), Pick(rng, "Summit Grocers", "FreshWay Market", "Hilltop Foods"), Next());
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && rng.NextDouble() < 0.35)
                Post(checking, TransactionType.Withdrawal, Money(3.75, 8.50), Pick(rng, "Peak Coffee Co.", "Basecamp Café"), Next());
            if (rng.NextDouble() < 0.15) Post(checking, TransactionType.Withdrawal, Money(30, 62), Pick(rng, "Ridgeway Gas", "Keystone Fuel"), Next());
            if (rng.NextDouble() < 0.12) Post(checking, TransactionType.Withdrawal, Money(18, 85), Pick(rng, "Altitude Bistro", "Main St. Diner", "Noodle Lab"), Next());
            if (rng.NextDouble() < 0.07) Post(checking, TransactionType.Withdrawal, Money(15, 120), Pick(rng, "Online order - ShopNest", "Main St. Pharmacy", "Trailhead Outfitters"), Next());
            if (rng.NextDouble() < 0.04) Post(checking, TransactionType.Withdrawal, Pick(rng, 40m, 60m, 100m), "ATM withdrawal", Next());
            if (rng.NextDouble() < 0.03) Post(checking, TransactionType.Deposit, Money(20, 300), Pick(rng, "Mobile check deposit", "Refund - ShopNest", "Venmo cash-out"), Next());
        }

        return transfers;

        // Records one transaction and updates the running balance. Debits that would overdraw are skipped.
        static Transaction? Post(Account account, TransactionType type, decimal amount, string description, DateTime when)
        {
            if (amount <= 0 || when.Date < account.OpenedAt.Date) return null;   // not open yet
            bool credit = Transaction.IsCreditType(type);
            if (!credit && amount > account.Balance) return null;
            account.Balance += credit ? amount : -amount;
            var t = new Transaction
            {
                Date = when, Type = type, Amount = amount, Description = description, BalanceAfter = account.Balance
            };
            account.Transactions.Add(t);
            return t;
        }

        void Transfer(Account from, Account to, decimal amount, DateTime when)
        {
            if (amount <= 0 || amount > from.Balance || when.Date < from.OpenedAt.Date || when.Date < to.OpenedAt.Date) return;
            var o = Post(from, TransactionType.TransferOut, amount, $"Transfer to {to.Nickname}", when);
            var i = Post(to, TransactionType.TransferIn, amount, $"Transfer from {from.Nickname}", when);
            if (o != null && i != null) transfers.Add((o, i));
        }
    }

    /// <summary>Links the two legs of each seeded transfer (counterparty account + matching row).</summary>
    private static void LinkTransfers(List<(Transaction Out, Transaction In)> transfers)
    {
        foreach (var (o, i) in transfers)
        {
            o.LinkedTransactionId = i.Id; o.CounterpartyAccountId = i.AccountId;
            i.LinkedTransactionId = o.Id; i.CounterpartyAccountId = o.AccountId;
        }
    }

    private static T Pick<T>(Random rng, params T[] options) => options[rng.Next(options.Length)];
}
