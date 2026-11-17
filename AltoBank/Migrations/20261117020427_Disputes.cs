using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltoBank.Migrations
{
    /// <inheritdoc />
    public partial class Disputes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ProvisionalCreditOnDisputes",
                table: "BankSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Disputes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    AccountId = table.Column<int>(type: "INTEGER", nullable: false),
                    TransactionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ExpectedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    DuplicateOfTransactionId = table.Column<int>(type: "INTEGER", nullable: true),
                    RefundAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Attachment = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false, defaultValue: "Submitted"),
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReviewStartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReviewerEmail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    DecisionReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ProvisionalCreditEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ProvisionalCreditTransactionId = table.Column<int>(type: "INTEGER", nullable: true),
                    ClawbackTransactionId = table.Column<int>(type: "INTEGER", nullable: true),
                    ClawbackOverdrew = table.Column<bool>(type: "INTEGER", nullable: false),
                    RefundTransactionId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Disputes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Disputes_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Disputes_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Disputes_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "BankSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "ProvisionalCreditOnDisputes",
                value: false);

            migrationBuilder.CreateIndex(
                name: "IX_Disputes_AccountId",
                table: "Disputes",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Disputes_Status_SubmittedAt",
                table: "Disputes",
                columns: new[] { "Status", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Disputes_TransactionId",
                table: "Disputes",
                column: "TransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Disputes_UserId_SubmittedAt",
                table: "Disputes",
                columns: new[] { "UserId", "SubmittedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Disputes");

            migrationBuilder.DropColumn(
                name: "ProvisionalCreditOnDisputes",
                table: "BankSettings");
        }
    }
}
