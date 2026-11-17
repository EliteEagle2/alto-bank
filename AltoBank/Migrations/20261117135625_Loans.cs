using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltoBank.Migrations
{
    /// <inheritdoc />
    public partial class Loans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AutoLoanRateDiscount",
                table: "BankSettings",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 2.50m);

            migrationBuilder.AddColumn<int>(
                name: "LoanGraceDays",
                table: "BankSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<decimal>(
                name: "LoanLateFee",
                table: "BankSettings",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 35m);

            migrationBuilder.AddColumn<decimal>(
                name: "LoanRateTierA",
                table: "BankSettings",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 8.99m);

            migrationBuilder.AddColumn<decimal>(
                name: "LoanRateTierB",
                table: "BankSettings",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 11.99m);

            migrationBuilder.AddColumn<decimal>(
                name: "LoanRateTierC",
                table: "BankSettings",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 15.99m);

            migrationBuilder.AddColumn<decimal>(
                name: "LoanRateTierD",
                table: "BankSettings",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 19.99m);

            migrationBuilder.AddColumn<decimal>(
                name: "LoanReviewAmount",
                table: "BankSettings",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 20000m);

            migrationBuilder.AddColumn<int>(
                name: "DisburseToAccountId",
                table: "AccountApplications",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCounterOffer",
                table: "AccountApplications",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "LoanAmount",
                table: "AccountApplications",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LoanAprPercent",
                table: "AccountApplications",
                type: "decimal(5,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LoanKind",
                table: "AccountApplications",
                type: "TEXT",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LoanMonthlyPayment",
                table: "AccountApplications",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LoanPurpose",
                table: "AccountApplications",
                type: "TEXT",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LoanRateTier",
                table: "AccountApplications",
                type: "TEXT",
                maxLength: 1,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LoanTermMonths",
                table: "AccountApplications",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OfferRespondedAt",
                table: "AccountApplications",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OfferedAt",
                table: "AccountApplications",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RequestedLoanAmount",
                table: "AccountApplications",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VehicleMake",
                table: "AccountApplications",
                type: "TEXT",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VehicleModel",
                table: "AccountApplications",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VehicleYear",
                table: "AccountApplications",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Loans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AccountId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    ApplicationId = table.Column<int>(type: "INTEGER", nullable: true),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Purpose = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    VehicleYear = table.Column<int>(type: "INTEGER", nullable: true),
                    VehicleMake = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    VehicleModel = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    Principal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AprPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    TermMonths = table.Column<int>(type: "INTEGER", nullable: false),
                    MonthlyPayment = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RateTier = table.Column<string>(type: "TEXT", maxLength: 1, nullable: true),
                    DisbursedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DisbursedToAccountId = table.Column<int>(type: "INTEGER", nullable: true),
                    FirstDueDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PrincipalRemaining = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    FeesOwed = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PaymentsMade = table.Column<int>(type: "INTEGER", nullable: false),
                    NextDueDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    LateFeeForDueDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    AutopayEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AutopayFromAccountId = table.Column<int>(type: "INTEGER", nullable: true),
                    AutopayAttemptedFor = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false, defaultValue: "Active"),
                    PaidOffAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Loans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Loans_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LoanPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LoanId = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ByAutopay = table.Column<bool>(type: "INTEGER", nullable: false),
                    InstallmentNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    PaidAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Interest = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Principal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Fees = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PrincipalAfter = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    FromAccountId = table.Column<int>(type: "INTEGER", nullable: true),
                    TransactionId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoanPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoanPayments_Loans_LoanId",
                        column: x => x.LoanId,
                        principalTable: "Loans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "BankSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "AutoLoanRateDiscount", "LoanGraceDays", "LoanLateFee", "LoanRateTierA", "LoanRateTierB", "LoanRateTierC", "LoanRateTierD", "LoanReviewAmount" },
                values: new object[] { 2.50m, 10, 35m, 8.99m, 11.99m, 15.99m, 19.99m, 20000m });

            migrationBuilder.CreateIndex(
                name: "IX_LoanPayments_LoanId_PaidAt",
                table: "LoanPayments",
                columns: new[] { "LoanId", "PaidAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Loans_AccountId",
                table: "Loans",
                column: "AccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Loans_ApplicationId",
                table: "Loans",
                column: "ApplicationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Loans_UserId_Status",
                table: "Loans",
                columns: new[] { "UserId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LoanPayments");

            migrationBuilder.DropTable(
                name: "Loans");

            migrationBuilder.DropColumn(
                name: "AutoLoanRateDiscount",
                table: "BankSettings");

            migrationBuilder.DropColumn(
                name: "LoanGraceDays",
                table: "BankSettings");

            migrationBuilder.DropColumn(
                name: "LoanLateFee",
                table: "BankSettings");

            migrationBuilder.DropColumn(
                name: "LoanRateTierA",
                table: "BankSettings");

            migrationBuilder.DropColumn(
                name: "LoanRateTierB",
                table: "BankSettings");

            migrationBuilder.DropColumn(
                name: "LoanRateTierC",
                table: "BankSettings");

            migrationBuilder.DropColumn(
                name: "LoanRateTierD",
                table: "BankSettings");

            migrationBuilder.DropColumn(
                name: "LoanReviewAmount",
                table: "BankSettings");

            migrationBuilder.DropColumn(
                name: "DisburseToAccountId",
                table: "AccountApplications");

            migrationBuilder.DropColumn(
                name: "IsCounterOffer",
                table: "AccountApplications");

            migrationBuilder.DropColumn(
                name: "LoanAmount",
                table: "AccountApplications");

            migrationBuilder.DropColumn(
                name: "LoanAprPercent",
                table: "AccountApplications");

            migrationBuilder.DropColumn(
                name: "LoanKind",
                table: "AccountApplications");

            migrationBuilder.DropColumn(
                name: "LoanMonthlyPayment",
                table: "AccountApplications");

            migrationBuilder.DropColumn(
                name: "LoanPurpose",
                table: "AccountApplications");

            migrationBuilder.DropColumn(
                name: "LoanRateTier",
                table: "AccountApplications");

            migrationBuilder.DropColumn(
                name: "LoanTermMonths",
                table: "AccountApplications");

            migrationBuilder.DropColumn(
                name: "OfferRespondedAt",
                table: "AccountApplications");

            migrationBuilder.DropColumn(
                name: "OfferedAt",
                table: "AccountApplications");

            migrationBuilder.DropColumn(
                name: "RequestedLoanAmount",
                table: "AccountApplications");

            migrationBuilder.DropColumn(
                name: "VehicleMake",
                table: "AccountApplications");

            migrationBuilder.DropColumn(
                name: "VehicleModel",
                table: "AccountApplications");

            migrationBuilder.DropColumn(
                name: "VehicleYear",
                table: "AccountApplications");
        }
    }
}
