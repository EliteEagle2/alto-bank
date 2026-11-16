using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltoBank.Migrations
{
    /// <inheritdoc />
    public partial class MobileCheckDeposit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MobileDepositDailyLimit",
                table: "BankSettings",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 10000m);   // the existing settings row gets the real defaults, not $0 (which would refuse every check)

            migrationBuilder.AddColumn<decimal>(
                name: "MobileDepositPerCheckLimit",
                table: "BankSettings",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 5000m);

            migrationBuilder.CreateTable(
                name: "CheckDeposits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    AccountId = table.Column<int>(type: "INTEGER", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    FrontImage = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    BackImage = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FrontHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false, defaultValue: "Pending"),
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReviewerEmail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    RejectReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    TransactionId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckDeposits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckDeposits_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CheckDeposits_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "BankSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "MobileDepositDailyLimit", "MobileDepositPerCheckLimit" },
                values: new object[] { 10000m, 5000m });

            migrationBuilder.CreateIndex(
                name: "IX_CheckDeposits_AccountId",
                table: "CheckDeposits",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckDeposits_FrontHash",
                table: "CheckDeposits",
                column: "FrontHash");

            migrationBuilder.CreateIndex(
                name: "IX_CheckDeposits_Status_SubmittedAt",
                table: "CheckDeposits",
                columns: new[] { "Status", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CheckDeposits_UserId_SubmittedAt",
                table: "CheckDeposits",
                columns: new[] { "UserId", "SubmittedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CheckDeposits");

            migrationBuilder.DropColumn(
                name: "MobileDepositDailyLimit",
                table: "BankSettings");

            migrationBuilder.DropColumn(
                name: "MobileDepositPerCheckLimit",
                table: "BankSettings");
        }
    }
}
