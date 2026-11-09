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

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder); // required so Identity can configure its tables

        // Account numbers must be unique across the bank.
        builder.Entity<Account>().HasIndex(a => a.AccountNumber).IsUnique();

        // Store enums as readable text ("Checking") instead of numbers.
        builder.Entity<Account>().Property(a => a.AccountType).HasConversion<string>();
        builder.Entity<Transaction>().Property(t => t.Type).HasConversion<string>();

        // User -> Accounts -> Transactions (deleting a parent deletes its children).
        builder.Entity<Account>()
            .HasOne(a => a.User).WithMany(u => u.Accounts)
            .HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Transaction>()
            .HasOne(t => t.Account).WithMany(a => a.Transactions)
            .HasForeignKey(t => t.AccountId).OnDelete(DeleteBehavior.Cascade);

        // User -> Applications. Each application may point at the Account it opened.
        builder.Entity<AccountApplication>(app =>
        {
            app.Property(a => a.Status).HasConversion<string>();
            app.Property(a => a.AccountType).HasConversion<string>();
            app.HasOne(a => a.User).WithMany(u => u.Applications)
               .HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
            app.HasOne(a => a.Account).WithMany()
               .HasForeignKey(a => a.AccountId).OnDelete(DeleteBehavior.SetNull);
            app.HasIndex(a => new { a.UserId, a.SubmittedAt });
        });
    }
}
