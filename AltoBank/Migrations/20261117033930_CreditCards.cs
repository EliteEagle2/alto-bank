using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltoBank.Migrations
{
    /// <inheritdoc />
    public partial class CreditCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CardCashBackRate",
                table: "BankSettings",
                type: "decimal(6,4)",
                nullable: false,
                defaultValue: 0.015m);

            migrationBuilder.AddColumn<decimal>(
                name: "CardLateFee",
                table: "BankSettings",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 29m);

            migrationBuilder.AddColumn<decimal>(
                name: "CardMinimumPaymentFloor",
                table: "BankSettings",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 25m);

            migrationBuilder.AddColumn<decimal>(
                name: "CardMinimumPaymentPercent",
                table: "BankSettings",
                type: "decimal(6,4)",
                nullable: false,
                defaultValue: 0.02m);

            migrationBuilder.AddColumn<decimal>(
                name: "CardPurchaseAprPercent",
                table: "BankSettings",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 21.99m);

            migrationBuilder.AddColumn<string>(
                name: "Autopay",
                table: "Accounts",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<int>(
                name: "AutopayFromAccountId",
                table: "Accounts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CreditLimit",
                table: "Accounts",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "NextStatementDate",
                table: "Accounts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RewardsBalance",
                table: "Accounts",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "StatementCloseDay",
                table: "Accounts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AgreedCardmemberAgreement",
                table: "AccountApplications",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "CreditLimit",
                table: "AccountApplications",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DecisionNote",
                table: "AccountApplications",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlyHousingPayment",
                table: "AccountApplications",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CreditCardStatements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AccountId = table.Column<int>(type: "INTEGER", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PreviousBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Purchases = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PaymentsAndCredits = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    InterestCharged = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    FeesCharged = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    StatementBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MinimumPayment = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    AprPercent = table.Column<decimal>(type: "decimal(4,2)", nullable: false),
                    LastTransactionId = table.Column<int>(type: "INTEGER", nullable: false),
                    DueDateProcessed = table.Column<bool>(type: "INTEGER", nullable: false),
                    AutopayAttemptedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AutopayResult = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    LateFeeCharged = table.Column<bool>(type: "INTEGER", nullable: false),
                    LateFeeTransactionId = table.Column<int>(type: "INTEGER", nullable: true),
                    LateFeeWaived = table.Column<bool>(type: "INTEGER", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditCardStatements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CreditCardStatements_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "BankSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CardCashBackRate", "CardLateFee", "CardMinimumPaymentFloor", "CardMinimumPaymentPercent", "CardPurchaseAprPercent" },
                values: new object[] { 0.015m, 29m, 25m, 0.02m, 21.99m });

            migrationBuilder.CreateIndex(
                name: "IX_CreditCardStatements_AccountId_PeriodEnd",
                table: "CreditCardStatements",
                columns: new[] { "AccountId", "PeriodEnd" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreditCardStatements_DueDate",
                table: "CreditCardStatements",
                column: "DueDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CreditCardStatements");

            migrationBuilder.DropColumn(
                name: "CardCashBackRate",
                table: "BankSettings");

            migrationBuilder.DropColumn(
                name: "CardLateFee",
                table: "BankSettings");

            migrationBuilder.DropColumn(
                name: "CardMinimumPaymentFloor",
                table: "BankSettings");

            migrationBuilder.DropColumn(
                name: "CardMinimumPaymentPercent",
                table: "BankSettings");

            migrationBuilder.DropColumn(
                name: "CardPurchaseAprPercent",
                table: "BankSettings");

            migrationBuilder.DropColumn(
                name: "Autopay",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "AutopayFromAccountId",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "CreditLimit",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "NextStatementDate",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "RewardsBalance",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "StatementCloseDay",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "AgreedCardmemberAgreement",
                table: "AccountApplications");

            migrationBuilder.DropColumn(
                name: "CreditLimit",
                table: "AccountApplications");

            migrationBuilder.DropColumn(
                name: "DecisionNote",
                table: "AccountApplications");

            migrationBuilder.DropColumn(
                name: "MonthlyHousingPayment",
                table: "AccountApplications");
        }
    }
}
