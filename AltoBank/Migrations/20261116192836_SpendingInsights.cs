using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltoBank.Migrations
{
    /// <inheritdoc />
    public partial class SpendingInsights : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_AccountId",
                table: "Transactions");

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Transactions",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "Other");

            migrationBuilder.AddColumn<bool>(
                name: "CategoryOverridden",
                table: "Transactions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Budgets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    MonthlyLimit = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false, defaultValue: "Active"),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Budgets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Budgets_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_AccountId_Category_Date",
                table: "Transactions",
                columns: new[] { "AccountId", "Category", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_UserId_Category",
                table: "Budgets",
                columns: new[] { "UserId", "Category" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Budgets");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_AccountId_Category_Date",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "CategoryOverridden",
                table: "Transactions");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_AccountId",
                table: "Transactions",
                column: "AccountId");
        }
    }
}
