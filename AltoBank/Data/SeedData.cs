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
    /// <summary>The OWNER (founder): every admin permission plus managing staff. Exactly one, set in configuration
    /// (Bank:OwnerEmail, e.g. Bank__OwnerEmail in .env). Not configured = the seeded admin is the owner.</summary>
    public const string OwnerRole = "Owner";
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
        // The admin (staff) also banks here: their own accounts, used through the normal customer site.
        // Admin tools refuse to act on these (AdminService.ExecuteAsync blocks self-service changes).
        new(AdminEmail, "Alex", "J", "Morgan", new DateOnly(1985, 7, 22), "(814) 555-0108",
            "88 Logan Blvd", null, "Altoona", "PA", "16602",
            "Alto Bank", "Operations Manager", "$100,000 – $199,999",
            Paycheck: 2650m, Rent: 1400m, RandomSeed: 44,
            Accounts: new SeedAccount[]
            {
                new("7740315520", AccountType.Checking, "Personal Checking", 1800m, 60, CardDesign: CardDesigns.Onyx),
                new("7740318841", AccountType.Savings, "Rainy Day Fund", 3000m, 45, GoalName: "Six months of expenses", GoalAmount: 15000m),
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

        // WAL (write-ahead logging) journal mode: readers don't block the writer and vice versa, which matters
        // once the background scheduler writes while people use the site. It's stored in the database file.
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;");

        if (!await roleManager.RoleExistsAsync(CustomerRole))
            await roleManager.CreateAsync(new IdentityRole(CustomerRole));
        await EnsureAdminAsync(userManager, roleManager);
        await EnsureOwnerAsync(userManager, roleManager, services.GetRequiredService<IConfiguration>());

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
        // Today's demo activity (P2P payments, requests, bill payments) is created through the real services, so it
        // is all stamped "now"; spread it across the bank day afterwards (only on a fresh seed).
        var seedStarted = DateTime.UtcNow;
        var freshPayments = !await db.Payments.AnyAsync();
        await EnsurePaymentsDemoDataAsync(services, db, userManager);
        await EnsureBillPayDemoDataAsync(services, db, userManager);
        if (freshPayments) await SpreadTodaysDemoActivityAsync(db, seedStarted);
        await EnsurePreferencesDemoDataAsync(db, userManager);
        await CategorizeTransactionsAsync(db);
        await EnsureInsightsDemoDataAsync(services, db, userManager);
        await EnsureCheckDepositDemoDataAsync(services, db, userManager);
        await EnsureDisputeDemoDataAsync(services, db, userManager);
    }

    /// <summary>
    /// Disputes demo, made through the real flows (IDisputeService to file, IAdminService to decide), so the money,
    /// alerts and audit rows are exactly what the app would produce:
    ///   Jordan: one approved (refunded through the reversal system) and one under review ("Wrong amount").
    ///   Maya:   one denied, filed while provisional credit was ON, so the credit and its clawback are both visible.
    /// </summary>
    private static async Task EnsureDisputeDemoDataAsync(IServiceProvider services, ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        if (await db.Disputes.AnyAsync()) return;
        var jordan = await userManager.FindByEmailAsync(DemoEmail);
        var maya = await userManager.FindByEmailAsync("maya@altobank.com");
        if (jordan == null || maya == null) return;
        var disputes = services.GetRequiredService<AltoBank.Services.IDisputeService>();
        var admin = services.GetRequiredService<AltoBank.Areas.Admin.Services.IAdminService>();
        var since = DateTime.UtcNow.AddDays(-45);

        // Recent card purchases (withdrawals) on each customer's main checking, newest first.
        // (The amount test is done in C#: SQLite stores decimals as text, so "Amount >= 15" in SQL wouldn't filter anything.)
        async Task<List<Transaction>> Purchases(string userId, string number) =>
            (await db.Transactions.AsNoTracking()
                .Where(t => t.Account!.UserId == userId && t.Account.AccountNumber == number && t.Type == TransactionType.Withdrawal
                            && t.Status == TransactionStatus.Posted && t.Date >= since)
                .OrderByDescending(t => t.Date).ToListAsync())
            .Where(t => t.Amount >= 15).ToList();
        var jp = await Purchases(jordan.Id, "4410274821");
        var mp = await Purchases(maya.Id, "5520831402");
        if (jp.Count < 2 || mp.Count < 1) { Console.WriteLine("  Disputes demo: not enough recent purchases; skipped."); return; }

        // The staff decisions are recorded as the seeded admin.
        using var asAdmin = AltoBank.Services.AuditLogger.ActAs(new AltoBank.Services.AuditActor(null, AdminEmail, "Admin"));
        async Task<Dispute?> File(string userId, Transaction t, DisputeReason reason, string text, decimal? expected = null)
        {
            var (r, d) = await disputes.SubmitAsync(userId, new AltoBank.Services.DisputeInput(t.Id, reason, text, expected, null, false), null);
            if (!r.Success) Console.WriteLine($"  Disputes demo: couldn't file one ({r.Message}).");
            return d;
        }

        // Jordan 1: an online order that never arrived -> approved, refunded in full.
        var monthStart = AltoBank.Services.BankTime.StartOfMonthUtc(AltoBank.Services.BankTime.Today.Year, AltoBank.Services.BankTime.Today.Month);
        var lastMonth = jp.Where(t => t.Date < monthStart).ToList();
        var order = lastMonth.FirstOrDefault(t => t.Description.Contains("ShopNest") || t.Description.Contains("Outfitters"))
                    ?? lastMonth.FirstOrDefault() ?? jp[0];
        if (await File(jordan.Id, order, DisputeReason.NotReceived,
                "I ordered this online but the package never arrived, and the store stopped answering my emails.") is { } refunded)
        {
            await admin.StartDisputeReviewAsync(refunded.Id, "Checking the order with the merchant");
            await admin.ApproveDisputeAsync(refunded.Id, "The merchant couldn't show a delivery, so we refunded the full amount.");
        }

        // Jordan 2: charged more than expected -> under review.
        var meal = jp.FirstOrDefault(t => t.Id != order.Id && t.Amount >= 25) ?? jp.First(t => t.Id != order.Id);
        if (await File(jordan.Id, meal, DisputeReason.WrongAmount,
                "The receipt I signed was for less than this. I think the tip was added twice.", expected: Math.Round(meal.Amount - 10m, 2)) is { } review)
            await admin.StartDisputeReviewAsync(review.Id, "Asked the merchant for the itemized receipt");

        // Maya: filed while provisional credit was ON (credited right away), then denied (credit taken back).
        var settings = await db.BankSettings.FirstAsync();
        var was = settings.ProvisionalCreditOnDisputes;
        settings.ProvisionalCreditOnDisputes = true;
        await db.SaveChangesAsync();
        var charge = mp.First();
        var denied = await File(maya.Id, charge, DisputeReason.NotAuthorized,
            "I don't recognize this purchase. I wasn't in the store that day and still have my card.");
        settings.ProvisionalCreditOnDisputes = was;
        await db.SaveChangesAsync();
        if (denied != null)
        {
            await admin.StartDisputeReviewAsync(denied.Id, "Requested the store's receipt");
            await admin.DenyDisputeAsync(denied.Id, "The store sent a receipt signed with your card's chip and PIN at the time of purchase");
        }

        // Show the staff member who decided, as on a real admin action.
        foreach (var d in await db.Disputes.Where(d => d.ReviewerEmail == "admin").ToListAsync()) d.ReviewerEmail = AdminEmail;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Mobile check deposit demo: placeholder check images drawn in code (SampleCheckImage: clearly marked
    /// "SAMPLE - NOT A REAL CHECK", no real bank or routing details), stored through IFileStore like real uploads.
    ///   Jordan: one approved (posted through IBankService, so his running balance stays right) and one pending.
    ///   Maya:   one rejected because the back wasn't endorsed.   Samuel: none.
    /// </summary>
    private static async Task EnsureCheckDepositDemoDataAsync(IServiceProvider services, ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        if (await db.CheckDeposits.AnyAsync()) return;
        var jordan = await userManager.FindByEmailAsync(DemoEmail);
        var maya = await userManager.FindByEmailAsync("maya@altobank.com");
        var jChk = await db.Accounts.FirstOrDefaultAsync(a => a.AccountNumber == "4410274821");
        var mChk = await db.Accounts.FirstOrDefaultAsync(a => a.AccountNumber == "5520831402");
        if (jordan == null || maya == null || jChk == null || mChk == null) return;

        var files = services.GetRequiredService<AltoBank.Services.IFileStore>();
        var bank = services.GetRequiredService<AltoBank.Services.IBankService>();
        var now = DateTime.UtcNow;
        var today = AltoBank.Services.BankTime.Today;

        async Task<(string Name, string Hash)> Store(byte[] png)
        {
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(png)).ToLowerInvariant();
            return (await files.SaveAsync(new AltoBank.Services.UploadedImage(png, ".png", "image/png", hash)), hash);
        }
        async Task<CheckDeposit> Make(ApplicationUser who, Account to, SampleCheckImage.Details d, bool endorsed, DateTime submitted)
        {
            var front = await Store(SampleCheckImage.Front(d));
            var back = await Store(SampleCheckImage.Back(d, endorsed));
            var dep = new CheckDeposit
            {
                UserId = who.Id, AccountId = to.Id, Amount = d.Amount, FrontImage = front.Name, BackImage = back.Name,
                FrontHash = front.Hash, SubmittedAt = submitted
            };
            db.CheckDeposits.Add(dep);
            await db.SaveChangesAsync();
            db.Alerts.Add(new Alert { UserId = who.Id, Type = AlertType.CheckReceived, Title = "Check received", CreatedAt = submitted, ReadAt = submitted.AddHours(1),
                Message = $"We got your {d.Amount:C} check for {to.DisplayName} ••{to.Last4}. We'll review it, usually within one business day.", Link = $"/checks/{dep.Id}" });
            return dep;
        }

        // Jordan: approved yesterday's check (posted now through IBankService, inside a transaction like the admin approval).
        var approved = await Make(jordan, jChk, new("Ridgeline Ski Club", "Jordan Rivera", 250.00m, "1042", today.AddDays(-2), "Lift ticket refund"),
                                  endorsed: true, submitted: now.AddDays(-1));
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            var (posted, t) = await bank.PostCheckDepositInCurrentTransactionAsync(approved);
            if (!posted.Success || t == null)
                Console.WriteLine($"  Check deposit demo: couldn't post {approved.Reference} ({posted.Message}); it stays Pending.");
            else
            {
                approved.Status = CheckDepositStatus.Approved;
                approved.ReviewedAt = t.Date;
                approved.ReviewerEmail = AdminEmail;
                approved.TransactionId = t.Id;
                db.Alerts.Add(new Alert { UserId = jordan.Id, Type = AlertType.CheckApproved, Title = "Check deposited", CreatedAt = t.Date,
                    Message = $"Your {approved.Amount:C} check was approved and added to {jChk.DisplayName} ••{jChk.Last4}.", Link = $"/checks/{approved.Id}" });
                await db.SaveChangesAsync();
                await tx.CommitAsync();
            }
        }

        // Jordan: one waiting for review (shows as "Pending" on his checking account and in the admin queue).
        await Make(jordan, jChk, new("Hilltop Property Mgmt", "Jordan Rivera", 480.00m, "5520", today, "Security deposit refund"),
                   endorsed: true, submitted: now.AddMinutes(-50));

        // Maya: rejected, the back wasn't endorsed.
        var rejected = await Make(maya, mChk, new("Center City Books", "Maya Chen", 300.00m, "0877", today.AddDays(-4), "Book club event"),
                                  endorsed: false, submitted: now.AddDays(-3));
        rejected.Status = CheckDepositStatus.Rejected;
        rejected.ReviewedAt = now.AddDays(-2);
        rejected.ReviewerEmail = AdminEmail;
        rejected.RejectReason = "The back isn't endorsed. Sign the back, write \"For mobile deposit only at Alto Bank\" under your signature, and deposit it again.";
        db.Alerts.Add(new Alert { UserId = maya.Id, Type = AlertType.CheckRejected, Title = "Check not accepted", CreatedAt = rejected.ReviewedAt.Value,
            Message = $"Your {rejected.Amount:C} check for {mChk.DisplayName} wasn't accepted: the back isn't endorsed. No money was added.", Link = $"/checks/{rejected.Id}" });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// SPENDING INSIGHTS backfill: transactions saved before categories existed got "Other" from the migration.
    /// Give each one its automatic category (CategoryRules), and each reversal the category of what it undid.
    /// A customer's own choice (CategoryOverridden) is never touched. Only rows that change are saved. Rows that
    /// really are "Other" (ATM withdrawals, for example) are looked at again on each startup; that's a quick pass.
    /// </summary>
    private static async Task CategorizeTransactionsAsync(ApplicationDbContext db)
    {
        var rows = await db.Transactions.Where(t => !t.CategoryOverridden && t.Category == TransactionCategory.Other).ToListAsync();
        if (rows.Count == 0) return;
        // Ordinary rows first...
        foreach (var t in rows.Where(t => t.ReversalOfTransactionId == null))
            t.Category = CategoryRules.For(t.Type, t.Description);
        // ...then each reversal copies its original's category. The original may have been fixed in the loop above
        // (it's tracked in this same context), so look it up there before going to the database.
        var reversed = rows.Where(t => t.ReversalOfTransactionId != null).Select(t => t.ReversalOfTransactionId!.Value).ToList();
        var originals = await db.Transactions.Where(t => reversed.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Category);
        foreach (var t in rows.Where(t => t.ReversalOfTransactionId != null))
            t.Category = originals.TryGetValue(t.ReversalOfTransactionId!.Value, out var c) ? c : CategoryRules.For(t.Type, t.Description);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Demo budgets so the Insights page, the Budgets page and the dashboard widget have something to show.
    /// Limits are set from what each customer has ACTUALLY spent so far this month, so every reseed shows the
    /// three states: comfortably under (green), near the limit (amber, 75%+) and over (red, 100%+).
    /// Jordan also has one transaction he recategorized himself (the concert tickets he paid Maya for).
    /// Alex (staff) has none, which shows the empty state.
    /// </summary>
    private static async Task EnsureInsightsDemoDataAsync(IServiceProvider services, ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        if (await db.Budgets.AnyAsync()) return;
        var jordan = await userManager.FindByEmailAsync(DemoEmail);
        var maya = await userManager.FindByEmailAsync("maya@altobank.com");
        var sam = await userManager.FindByEmailAsync("sam@altobank.com");
        if (jordan == null || maya == null || sam == null) return;

        // Jordan paid Maya back for concert tickets: a person-to-person payment ("Transfers" by default) that is
        // really entertainment, so he moved it.
        var concert = await db.Transactions.Where(t => t.Account!.UserId == jordan.Id && t.Type == TransactionType.PaymentSent
                                                       && t.Description.Contains("Concert")).FirstOrDefaultAsync();
        if (concert != null) { concert.Category = TransactionCategory.Entertainment; concert.CategoryOverridden = true; }
        await db.SaveChangesAsync();

        var budgetsService = services.GetRequiredService<AltoBank.Services.IBudgetService>();
        foreach (var (user, plan) in new[]
        {
            (jordan, new[] { (TransactionCategory.FoodAndDining, "near"), (TransactionCategory.Groceries, "ok"), (TransactionCategory.Gas, "ok"),
                             (TransactionCategory.Shopping, "over"), (TransactionCategory.Entertainment, "near") }),
            (maya, new[] { (TransactionCategory.Groceries, "near"), (TransactionCategory.FoodAndDining, "ok"), (TransactionCategory.Gas, "over") }),
            (sam, new[] { (TransactionCategory.FoodAndDining, "over"), (TransactionCategory.Groceries, "ok") }),
        })
        {
            var month = AltoBank.Services.BankTime.FirstOfThisMonth;
            // Same spending rule as the Insights page (AltoBank.Services.Spending): debits minus refunds.
            var spent = AltoBank.Services.Spending.ByCategory(await AltoBank.Services.Spending.LoadAsync(db, user.Id, month, month.AddMonths(1)));
            var before = AltoBank.Services.Spending.ByCategory(await AltoBank.Services.Spending.LoadAsync(db, user.Id, month.AddMonths(-1), month));
            foreach (var (category, state) in plan)
            {
                var mtd = spent.GetValueOrDefault(category);
                var prev = before.GetValueOrDefault(category);
                static decimal Up5(decimal v) => Math.Max(5m, Math.Ceiling(v / 5m) * 5m);
                static decimal Down5(decimal v) => Math.Max(5m, Math.Floor(v / 5m) * 5m);
                var limit = mtd <= 0 ? Up5(Math.Max(prev, 50m))           // nothing spent yet this month: a normal-looking limit
                    : state switch
                    {
                        "over" => mtd > 5m ? Down5(mtd * 0.9m) : 5m,
                        "near" => Up5(mtd / 0.85m),
                        _ => Math.Ceiling(Math.Max(mtd / 0.45m, prev) / 25m) * 25m
                    };
                await budgetsService.SaveAsync(user.Id, category, limit);   // also raises the 75% / 100% alerts
            }
        }
    }

    /// <summary>
    /// Spreads the demo activity created "now" (P2P payments, requests, bill payments, their alerts and runs) across
    /// today's bank hours so the activity feed reads like a real day: events about 2 hours apart, the last one a few
    /// minutes ago, never before 7 AM or after 10 PM bank time (closer together when the day has less room; before
    /// 7 AM the events go on yesterday). Order is kept, so running balances stay correct. Both legs of a payment
    /// (sent + received) are one event and get the same time.
    /// </summary>
    private static async Task SpreadTodaysDemoActivityAsync(ApplicationDbContext db, DateTime seededFrom)
    {
        var txs = await db.Transactions.Where(t => t.Date >= seededFrom).OrderBy(t => t.Id).ToListAsync();
        if (txs.Count == 0) return;

        // Group into events: a PaymentReceived right after the matching PaymentSent is the same payment.
        var events = new List<List<Transaction>>();
        foreach (var t in txs)
        {
            var last = events.LastOrDefault();
            if (last is { Count: 1 } && t.Type == TransactionType.PaymentReceived
                && last[0].Type == TransactionType.PaymentSent && last[0].Amount == t.Amount)
                last.Add(t);
            else
                events.Add(new List<Transaction> { t });
        }

        // The window: today 7 AM .. min(now, 10 PM) in bank time; if that's too short (early morning), yesterday 7-10 PM.
        var now = DateTime.UtcNow;
        var today = AltoBank.Services.BankTime.Today;
        var start = AltoBank.Services.BankTime.StartOfDayUtc(today).AddHours(7);
        var end = new[] { now.AddMinutes(-5), AltoBank.Services.BankTime.StartOfDayUtc(today).AddHours(22) }.Min();
        if (end - start < TimeSpan.FromHours(1))
        {
            start = AltoBank.Services.BankTime.StartOfDayUtc(today.AddDays(-1)).AddHours(7);
            end = AltoBank.Services.BankTime.StartOfDayUtc(today.AddDays(-1)).AddHours(22);
        }
        var gap = TimeSpan.FromTicks(Math.Min(TimeSpan.FromHours(2).Ticks, (end - start).Ticks / events.Count));

        // New time per event, counting back from the end of the window; remember old -> new for everything else.
        var moved = new List<(DateTime Old, DateTime New)>();
        for (int i = 0; i < events.Count; i++)
        {
            var when = end - TimeSpan.FromTicks(gap.Ticks * (events.Count - 1 - i));
            foreach (var t in events[i]) { moved.Add((t.Date, when)); t.Date = when; }
        }
        // Any other record made during the seed takes the new time of the transaction closest to its own.
        DateTime Shift(DateTime old) => moved.OrderBy(m => Math.Abs((m.Old - old).Ticks)).First() is var m ? m.New + (old - m.Old) : old;

        foreach (var p in await db.Payments.Where(p => p.CreatedAt >= seededFrom).ToListAsync()) p.CreatedAt = Shift(p.CreatedAt);
        foreach (var r in await db.MoneyRequests.Where(r => r.CreatedAt >= seededFrom).ToListAsync())
        {
            var delta = Shift(r.CreatedAt) - r.CreatedAt;
            r.CreatedAt += delta; r.ExpiresAt += delta;
            if (r.RespondedAt is DateTime responded) r.RespondedAt = Shift(responded);
        }
        foreach (var run in await db.ScheduledRuns.Where(x => x.ProcessedAt >= seededFrom).ToListAsync()) run.ProcessedAt = Shift(run.ProcessedAt);
        foreach (var s in await db.ScheduledPayments.Where(x => x.LastRunAt >= seededFrom).ToListAsync()) s.LastRunAt = Shift(s.LastRunAt!.Value);
        foreach (var a in await db.Alerts.Where(a => a.CreatedAt >= seededFrom).ToListAsync())
        {
            var wasRead = a.ReadAt == a.CreatedAt;   // muted alerts arrive already read
            a.CreatedAt = Shift(a.CreatedAt);
            if (wasRead) a.ReadAt = a.CreatedAt;
        }
        await db.SaveChangesAsync();
    }

    /// <summary>PROFILE SETTINGS demo data (once): Jordan has balance alerts and a default account, Maya goes by "May"
    /// with the violet theme and budget alerts muted. Samuel keeps the defaults.</summary>
    private static async Task EnsurePreferencesDemoDataAsync(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        if (await db.UserPreferences.AnyAsync()) return;
        var jordan = await userManager.FindByEmailAsync(DemoEmail);
        var maya = await userManager.FindByEmailAsync("maya@altobank.com");
        if (jordan == null || maya == null) return;
        var jChk = await db.Accounts.Where(a => a.AccountNumber == "4410274821").Select(a => (int?)a.Id).FirstOrDefaultAsync();
        db.UserPreferences.AddRange(
            new UserPreferences { UserId = jordan.Id, DefaultAccountId = jChk, LowBalanceAlert = 250m, LargeDebitAlert = 2500m },
            new UserPreferences { UserId = maya.Id, PreferredName = "May", Theme = "violet", MutedAlerts = nameof(AlertCategory.Budgets) });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// BILL PAY demo data (added once, through the real services): payees for all three customers, monthly bills,
    /// Jordan's "move $50 to savings every Friday", one bill that was paid today (Jordan) and one that FAILED for
    /// insufficient funds (Samuel), so the alerts and the admin failures view have something to show.
    /// </summary>
    private static async Task EnsureBillPayDemoDataAsync(IServiceProvider services, ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        if (await db.Payees.AnyAsync()) return;
        var jordan = await userManager.FindByEmailAsync(DemoEmail);
        var maya = await userManager.FindByEmailAsync("maya@altobank.com");
        var sam = await userManager.FindByEmailAsync("sam@altobank.com");
        if (jordan == null || maya == null || sam == null) return;
        async Task<int?> Acct(string number) => await db.Accounts.Where(a => a.AccountNumber == number && a.Status == AccountStatus.Active).Select(a => (int?)a.Id).FirstOrDefaultAsync();
        var jChk = await Acct("4410274821"); var jSav = await Acct("4410279356");
        var mBills = await Acct("5520836617"); var sChk = await Acct("6630412257");
        if (jChk == null || jSav == null || mBills == null || sChk == null) return;

        var bills = services.GetRequiredService<AltoBank.Services.IBillPayService>();
        var bank = services.GetRequiredService<AltoBank.Services.IBankService>();
        var today = AltoBank.Services.BankTime.Today;
        async Task<int> Payee(ApplicationUser u, string name, PayeeCategory cat, string acct, string? nick = null) =>
            (await bills.AddPayeeAsync(u.Id, new AltoBank.Services.PayeeData(name, cat, acct, nick))).Payee!.Id;
        async Task<int?> Schedule(ApplicationUser u, ScheduleKind kind, int from, int? payee, int? to, decimal amount, string? memo,
                                  ScheduleFrequency freq, DayOfWeek? dow = null, int? dom = null) =>
            (await bills.CreateScheduleAsync(u.Id, new AltoBank.Services.ScheduleData(kind, from, payee, to, amount, memo, freq, today, dow, dom, null))).Item?.Id;

        // Jordan
        var water = await Payee(jordan, "Altoona Water Authority (demo)", PayeeCategory.Utilities, "AWA-20417", "Water");
        var electric = await Payee(jordan, "Keystone Electric (demo)", PayeeCategory.Utilities, "KE-88120344", "Electric");
        var phone = await Payee(jordan, "Summit Wireless (demo)", PayeeCategory.Phone, "SW-5521");
        await Schedule(jordan, ScheduleKind.BillPay, jChk.Value, electric, null, 86.40m, null, ScheduleFrequency.Monthly, dom: 15);
        await Schedule(jordan, ScheduleKind.BillPay, jChk.Value, water, null, 42.50m, null, ScheduleFrequency.Monthly, dom: 20);
        await Schedule(jordan, ScheduleKind.Transfer, jChk.Value, null, jSav.Value, 50m, "Savings goal", ScheduleFrequency.Weekly, dow: DayOfWeek.Friday);
        if (await Schedule(jordan, ScheduleKind.BillPay, jChk.Value, phone, null, 65.00m, "November bill", ScheduleFrequency.Once) is int paidToday)
            await bank.ExecuteScheduledAsync(paidToday, today);                          // paid right away (Succeeded run + alert)

        // Maya
        var rent = await Payee(maya, "Liberty Property Mgmt (demo)", PayeeCategory.Rent, "LPM-1700-12", "Rent");
        var mElectric = await Payee(maya, "Keystone Electric (demo)", PayeeCategory.Utilities, "KE-55310987");
        await Schedule(maya, ScheduleKind.BillPay, mBills.Value, rent, null, 1650m, null, ScheduleFrequency.Monthly, dom: 1);
        await Schedule(maya, ScheduleKind.BillPay, mBills.Value, mElectric, null, 74.10m, null, ScheduleFrequency.Monthly, dom: 12);

        // Samuel
        var insurance = await Payee(sam, "Keystone Mutual Insurance (demo)", PayeeCategory.Insurance, "KMI-77310", "Car insurance");
        var sPhone = await Payee(sam, "Summit Wireless (demo)", PayeeCategory.Phone, "SW-9031");
        await Schedule(sam, ScheduleKind.BillPay, sChk.Value, insurance, null, 128.00m, null, ScheduleFrequency.Monthly, dom: 24);
        if (await Schedule(sam, ScheduleKind.BillPay, sChk.Value, sPhone, null, 4000.00m, "Phone upgrade", ScheduleFrequency.Once) is int tooBig)
            await bank.ExecuteScheduledAsync(tooBig, today);                             // fails: insufficient funds (Failed run + alert)
    }

    /// <summary>
    /// SEND & REQUEST demo data (added once, through the real services so balances, alerts and audit rows are right):
    /// payments between all three customers, a request waiting in Jordan's inbox, a split bill from Samuel
    /// (Maya paid her share, Jordan hasn't), and a request Samuel declined.
    /// </summary>
    private static async Task EnsurePaymentsDemoDataAsync(IServiceProvider services, ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        if (await db.Payments.AnyAsync() || await db.MoneyRequests.AnyAsync()) return;
        var jordan = await userManager.FindByEmailAsync(DemoEmail);
        var maya = await userManager.FindByEmailAsync("maya@altobank.com");
        var sam = await userManager.FindByEmailAsync("sam@altobank.com");
        if (jordan == null || maya == null || sam == null) return;

        async Task<int?> Checking(string number) => await db.Accounts.Where(a => a.AccountNumber == number && a.Status == AccountStatus.Active).Select(a => (int?)a.Id).FirstOrDefaultAsync();
        var jChk = await Checking("4410274821");
        var mChk = await Checking("5520831402");
        var sChk = await Checking("6630412257");
        if (jChk == null || mChk == null || sChk == null) return;

        var bank = services.GetRequiredService<AltoBank.Services.IBankService>();
        var requests = services.GetRequiredService<AltoBank.Services.IPaymentRequestService>();
        await bank.SendPaymentAsync(jordan.Id, jChk.Value, maya.Email, 45.00m, "Concert tickets");
        await bank.SendPaymentAsync(maya.Id, mChk.Value, sam.PhoneNumber, 18.50m, "Lunch at Noodle Lab");
        await bank.SendPaymentAsync(sam.Id, sChk.Value, jordan.Email, 25.00m, "Gas money");

        // Pending: Maya asks Jordan for pizza money (shows in Jordan's inbox with Pay / Decline).
        await requests.CreateAsync(maya.Id, new AltoBank.Services.NewRequest(new() { new(jordan.Email, 30.00m) }, "Pizza night", null, false, false));

        // Split a bill: Samuel splits  of utilities evenly with Jordan and Maya (and himself):  each. Maya pays hers.
        var (_, split) = await requests.CreateAsync(sam.Id, new AltoBank.Services.NewRequest(
            new() { new(jordan.Email, 0m), new(maya.Email, 0m) }, "Utilities - October", 90.00m, true, true));
        var mayasShare = split.FirstOrDefault(r => r.PayerUserId == maya.Id);
        if (mayasShare != null) await bank.PayRequestAsync(maya.Id, mayasShare.Id, mChk.Value);

        // Declined: Jordan asked Samuel for movie money; Samuel declined.
        var (_, movie) = await requests.CreateAsync(jordan.Id, new AltoBank.Services.NewRequest(new() { new(sam.Email, 15.00m) }, "Movie tickets", null, false, false));
        if (movie.FirstOrDefault() is { } m) await requests.DeclineAsync(sam.Id, m.Id);
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
        // Staff can bank here too: the admin is ALSO a customer (own accounts on the customer site).
        if (!await userManager.IsInRoleAsync(admin, CustomerRole))
            await userManager.AddToRoleAsync(admin, CustomerRole);
    }

    /// <summary>
    /// The OWNER comes from configuration, never from the website, so no admin can make themselves owner:
    ///   Bank:OwnerEmail     who the owner is (default: the seeded admin, admin@altobank.com)
    ///   Bank:OwnerPassword  only used to CREATE that login if it doesn't exist yet (min. 8 chars, upper, lower, digit)
    ///   Bank:OwnerFirstName / Bank:OwnerLastName  (optional, for a newly created owner)
    /// Runs at every startup: the configured person gets Owner + Admin, and anyone else holding Owner loses it
    /// (they stay an Admin), so changing the setting and restarting hands ownership over.
    /// </summary>
    private static async Task EnsureOwnerAsync(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager, IConfiguration config)
    {
        if (!await roleManager.RoleExistsAsync(OwnerRole))
            await roleManager.CreateAsync(new IdentityRole(OwnerRole));

        var email = config["Bank:OwnerEmail"]?.Trim();
        if (string.IsNullOrEmpty(email)) email = AdminEmail;

        var owner = await userManager.FindByEmailAsync(email);
        if (owner == null)
        {
            var password = config["Bank:OwnerPassword"];
            if (string.IsNullOrEmpty(password))
            {
                Console.WriteLine($"  Owner: {email} has no login yet and Bank:OwnerPassword isn't set, so the seeded admin stays owner.");
                owner = await userManager.FindByEmailAsync(AdminEmail);
                if (owner == null) return;
            }
            else
            {
                owner = new ApplicationUser
                {
                    UserName = email, Email = email, EmailConfirmed = true, CreatedAt = DateTime.UtcNow,
                    FirstName = config["Bank:OwnerFirstName"] ?? "Bank", LastName = config["Bank:OwnerLastName"] ?? "Owner"
                };
                var created = await userManager.CreateAsync(owner, password);
                if (!created.Succeeded)
                    throw new InvalidOperationException("Could not create the owner from Bank:OwnerEmail/OwnerPassword: " +
                                                        string.Join(" ", created.Errors.Select(e => e.Description)));
            }
        }

        foreach (var role in new[] { AdminRole, OwnerRole })
            if (!await userManager.IsInRoleAsync(owner, role))
                await userManager.AddToRoleAsync(owner, role);
        foreach (var other in await userManager.GetUsersInRoleAsync(OwnerRole))
            if (other.Id != owner.Id)
                await userManager.RemoveFromRoleAsync(other, OwnerRole);
    }

    private static async Task EnsureCustomerAsync(ApplicationDbContext db, UserManager<ApplicationUser> userManager, SeedCustomer c)
    {
        var user = await userManager.FindByEmailAsync(c.Email);
        // "New" = no accounts yet: a brand-new seed user, or the admin the first time they become a customer.
        var isNew = user == null || !await db.Accounts.AnyAsync(a => a.UserId == user.Id);
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
                OpenedAt = BankDayAt(DateTime.UtcNow.Date.AddDays(-a.OpenedDaysAgo), 8)
            }).ToList();
            var transfers = GenerateHistory(c, accounts);
            db.Accounts.AddRange(accounts);
            await db.SaveChangesAsync();
            LinkTransfers(transfers);   // now that the rows have Ids
            await db.SaveChangesAsync();
        }
        if (user.CreatedAt == null)
        {
            user.CreatedAt = BankDayAt(DateTime.UtcNow.Date.AddDays(-c.Accounts.Max(a => a.OpenedDaysAgo)), 8);
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

    /// <summary>A time on the BANK-LOCAL day that <paramref name="day"/> falls on (e.g. 8 = 8:00 AM in Altoona), as UTC.</summary>
    private static DateTime BankDayAt(DateTime day, double localHour) =>
        AltoBank.Services.BankTime.StartOfDayUtc(DateOnly.FromDateTime(day)).AddHours(localHour);

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
                Post(account, TransactionType.Deposit, seed.OpeningDeposit, "Opening deposit", BankDayAt(date, 8));   // before any other activity that day
            if (day == 0) continue;
            // Each event this day gets a later timestamp, all within shop hours in BANK time (about 8:20 AM to 9:30 PM),
            // never in the middle of the night. Times never go backwards, so running balances stay in order.
            var at = BankDayAt(date, 8);
            var closing = BankDayAt(date, 21.5);
            DateTime Next() => at = at.AddMinutes(rng.Next(20, 95)) is var t && t > closing ? closing : t;

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
                    Post(s, TransactionType.Interest, Math.Round(s.Balance * 0.041m / 12m, 2), "Interest earned", BankDayAt(date, 21.75));

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
                Date = when, Type = type, Amount = amount, Description = description, BalanceAfter = account.Balance,
                Category = CategoryRules.For(type, description)
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
