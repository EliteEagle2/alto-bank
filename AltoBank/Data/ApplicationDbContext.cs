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
