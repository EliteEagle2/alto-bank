using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltoBank.Migrations
{
    /// <summary>
    /// Two new Spending Insights categories: "Fees & Interest" and "Debt payments". No schema change (Category is stored
    /// as text). Existing rows move to them unless the customer picked a category themselves (CategoryOverridden).
    /// The seeder's CategorizeTransactionsAsync then re-runs the merchant rules on what's still "Other" (bookshop,
    /// fitness, parking, hardware, dry cleaning).
    /// </summary>
    public partial class CategoriesFeesAndDebt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Interest charged and late fees, and the waivers that refund them.
            migrationBuilder.Sql(@"UPDATE Transactions SET Category = 'FeesAndInterest'
                WHERE CategoryOverridden = 0 AND Type IN ('InterestCharge', 'LateFee', 'FeeWaiver');");
            // Paying your own card or loan: the card/loan side and the checking/savings side.
            migrationBuilder.Sql(@"UPDATE Transactions SET Category = 'DebtPayments'
                WHERE CategoryOverridden = 0 AND (Type IN ('CardPayment', 'LoanPayment')
                   OR (Type = 'TransferOut' AND CounterpartyAccountId IN (SELECT Id FROM Accounts WHERE AccountType IN ('CreditCard', 'Loan'))));");
            // Reversals take the category of what they undo.
            migrationBuilder.Sql(@"UPDATE Transactions SET Category = (SELECT o.Category FROM Transactions o WHERE o.Id = Transactions.ReversalOfTransactionId)
                WHERE CategoryOverridden = 0 AND ReversalOfTransactionId IS NOT NULL
                  AND (SELECT o.Category FROM Transactions o WHERE o.Id = Transactions.ReversalOfTransactionId) IN ('FeesAndInterest', 'DebtPayments');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"UPDATE Transactions SET Category = 'Other' WHERE CategoryOverridden = 0 AND Category = 'FeesAndInterest';");
            migrationBuilder.Sql(@"UPDATE Transactions SET Category = 'Transfers' WHERE CategoryOverridden = 0 AND Category = 'DebtPayments';");
        }
    }
}
