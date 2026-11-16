using AltoBank.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AltoBank.Data;

/// <summary>
/// EF Core database context. Inheriting from IdentityDbContext gives us all the
/// Identity tables (AspNetUsers, AspNetRoles, ...) plus our own banking tables.
/// </summary>
public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<AccountApplication> AccountApplications => Set<AccountApplication>();
    public DbSet<Card> Cards => Set<Card>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<CustomerNote> CustomerNotes => Set<CustomerNote>();
    public DbSet<BankSetting> BankSettings => Set<BankSetting>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<MoneyRequest> MoneyRequests => Set<MoneyRequest>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<UserPreferences> UserPreferences => Set<UserPreferences>();
    public DbSet<Payee> Payees => Set<Payee>();
    public DbSet<ScheduledPayment> ScheduledPayments => Set<ScheduledPayment>();
    public DbSet<ScheduledRun> ScheduledRuns => Set<ScheduledRun>();
    public DbSet<SchedulerRunLog> SchedulerRunLogs => Set<SchedulerRunLog>();
    public DbSet<Budget> Budgets => Set<Budget>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder); // required so Identity can configure its tables

        // IMPORTANT: financial history must never disappear, so every relationship below uses
        // DeleteBehavior.Restrict — the database refuses to delete a parent that still has children.
        // (The app never hard-deletes anyway: accounts are Closed, transactions Reversed, cards Cancelled.)

        builder.Entity<Account>(a =>
        {
            a.HasIndex(x => x.AccountNumber).IsUnique();
            // Store enums as readable text ("Checking") instead of numbers.
            a.Property(x => x.AccountType).HasConversion<string>();
            // Default "Active" also fills in existing rows when the column is added by the migration.
            a.Property(x => x.Status).HasConversion<string>().HasMaxLength(10).HasDefaultValue(AccountStatus.Active);
            a.HasOne(x => x.User).WithMany(u => u.Accounts)
             .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Transaction>(t =>
        {
            t.Property(x => x.Type).HasConversion<string>();
            t.Property(x => x.Status).HasConversion<string>().HasMaxLength(10).HasDefaultValue(TransactionStatus.Posted);
            t.HasOne(x => x.Account).WithMany(a => a.Transactions)
             .HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            t.HasIndex(x => x.Date);
            // Existing rows get "Other" from the migration; the seeder then categorizes them with CategoryRules.
            // Sentinel = Other: EF sends every other value (including FoodAndDining, the enum's 0) instead of the DB default.
            t.Property(x => x.Category).HasConversion<string>().HasMaxLength(20)
             .HasDefaultValue(TransactionCategory.Other).HasSentinel(TransactionCategory.Other);
            t.HasIndex(x => new { x.AccountId, x.Category, x.Date });   // insights: one category over a month
        });

        // ----- Spending Insights: one budget per (customer, category); removing only sets Status -----
        builder.Entity<Budget>(b =>
        {
            b.Property(x => x.Category).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(10).HasDefaultValue(BudgetStatus.Active);
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.UserId, x.Category }).IsUnique();
        });

        builder.Entity<AccountApplication>(app =>
        {
            app.Property(a => a.Status).HasConversion<string>();
            app.Property(a => a.AccountType).HasConversion<string>();
            app.HasOne(a => a.User).WithMany(u => u.Applications)
               .HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Restrict);
            app.HasOne(a => a.Account).WithMany()
               .HasForeignKey(a => a.AccountId).OnDelete(DeleteBehavior.Restrict);
            app.HasIndex(a => new { a.UserId, a.SubmittedAt });
            app.HasIndex(a => a.Status);
        });

        builder.Entity<Card>(c =>
        {
            c.Property(x => x.Type).HasConversion<string>().HasMaxLength(10);
            c.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
            c.HasIndex(x => x.CardNumber).IsUnique();
            c.HasOne(x => x.Account).WithMany(a => a.Cards)
             .HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<CustomerNote>(n =>
        {
            n.HasOne(x => x.Customer).WithMany(u => u.Notes)
             .HasForeignKey(x => x.CustomerUserId).OnDelete(DeleteBehavior.Restrict);
            n.HasIndex(x => new { x.CustomerUserId, x.CreatedAt });
        });

        builder.Entity<AuditLog>(l =>
        {
            l.HasIndex(x => x.Timestamp);
            l.HasIndex(x => x.Action);
            l.HasIndex(x => x.ActorUserId);
        });

        // ----- Send & Request -----
        builder.Entity<Payment>(p =>
        {
            p.Property(x => x.Status).HasConversion<string>().HasMaxLength(10).HasDefaultValue(PaymentStatus.Completed);
            p.HasOne(x => x.Sender).WithMany().HasForeignKey(x => x.SenderUserId).OnDelete(DeleteBehavior.Restrict);
            p.HasOne(x => x.Recipient).WithMany().HasForeignKey(x => x.RecipientUserId).OnDelete(DeleteBehavior.Restrict);
            p.HasOne(x => x.FromAccount).WithMany().HasForeignKey(x => x.FromAccountId).OnDelete(DeleteBehavior.Restrict);
            p.HasOne(x => x.ToAccount).WithMany().HasForeignKey(x => x.ToAccountId).OnDelete(DeleteBehavior.Restrict);
            p.HasIndex(x => new { x.SenderUserId, x.CreatedAt });
            p.HasIndex(x => new { x.RecipientUserId, x.CreatedAt });
        });

        builder.Entity<MoneyRequest>(r =>
        {
            r.Property(x => x.Status).HasConversion<string>().HasMaxLength(10).HasDefaultValue(MoneyRequestStatus.Pending);
            r.HasOne(x => x.Requester).WithMany().HasForeignKey(x => x.RequesterUserId).OnDelete(DeleteBehavior.Restrict);
            r.HasOne(x => x.Payer).WithMany().HasForeignKey(x => x.PayerUserId).OnDelete(DeleteBehavior.Restrict);
            r.HasOne(x => x.Payment).WithMany().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
            r.HasIndex(x => new { x.PayerUserId, x.Status });
            r.HasIndex(x => new { x.RequesterUserId, x.Status });
            r.HasIndex(x => new { x.Status, x.ExpiresAt });   // the scheduler's "what expired?" query
        });

        builder.Entity<Alert>(a =>
        {
            a.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
            a.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            a.HasIndex(x => new { x.UserId, x.ReadAt });
            a.HasIndex(x => new { x.UserId, x.DedupeKey }).IsUnique();   // NULL keys never clash in SQLite
        });

        // ----- Bill Pay + scheduled transfers -----
        builder.Entity<Payee>(p =>
        {
            p.Property(x => x.Category).HasConversion<string>().HasMaxLength(12);
            p.Property(x => x.Status).HasConversion<string>().HasMaxLength(10).HasDefaultValue(PayeeStatus.Active);
            p.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            p.HasIndex(x => new { x.UserId, x.Status });
        });

        builder.Entity<ScheduledPayment>(s =>
        {
            s.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
            s.Property(x => x.Frequency).HasConversion<string>().HasMaxLength(10);
            s.Property(x => x.DayOfWeek).HasConversion<string>().HasMaxLength(10);
            s.Property(x => x.Status).HasConversion<string>().HasMaxLength(10).HasDefaultValue(ScheduleStatus.Active);
            s.Property(x => x.LastRunStatus).HasConversion<string>().HasMaxLength(10);
            s.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            s.HasOne(x => x.FromAccount).WithMany().HasForeignKey(x => x.FromAccountId).OnDelete(DeleteBehavior.Restrict);
            s.HasOne(x => x.ToAccount).WithMany().HasForeignKey(x => x.ToAccountId).OnDelete(DeleteBehavior.Restrict);
            s.HasOne(x => x.Payee).WithMany().HasForeignKey(x => x.PayeeId).OnDelete(DeleteBehavior.Restrict);
            s.HasIndex(x => new { x.Status, x.NextRunDate });   // the scheduler's "what's due?" query
            s.HasIndex(x => new { x.UserId, x.Status });
        });

        builder.Entity<ScheduledRun>(r =>
        {
            r.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
            r.HasOne(x => x.ScheduledPayment).WithMany(s => s.Runs).HasForeignKey(x => x.ScheduledPaymentId).OnDelete(DeleteBehavior.Restrict);
            // IDEMPOTENCY: one row per item per date. The money moves in the same transaction that inserts this
            // row, so the database itself refuses to pay the same date twice.
            r.HasIndex(x => new { x.ScheduledPaymentId, x.RunDate }).IsUnique();
            r.HasIndex(x => new { x.Status, x.ProcessedAt });
        });

        builder.Entity<SchedulerRunLog>().HasIndex(x => x.StartedAt);

        // Profile settings: one optional row per user (shares the user's key). Users are never deleted, so no cascade.
        builder.Entity<UserPreferences>(p =>
        {
            p.HasOne(x => x.User).WithOne().HasForeignKey<UserPreferences>(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        // Exactly one settings row, created by the migration with the default values.
        builder.Entity<BankSetting>().HasData(new BankSetting
        {
            Id = BankSetting.SingletonId,
            SavingsApy = 0.0410m,
            CheckingApy = 0.0000m,
            DefaultDailyTransferLimit = 5000m,
            LargeTransactionThreshold = 2500m,
            AutoApproveApplications = true
        });
    }
}
