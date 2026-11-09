using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltoBank.Migrations
{
    /// <inheritdoc />
    public partial class AccountOpeningDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "Accounts",
                newName: "OpenedAt");

            migrationBuilder.AddColumn<string>(
                name: "CardDesign",
                table: "Accounts",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GoalAmount",
                table: "Accounts",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoalName",
                table: "Accounts",
                type: "TEXT",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CardDesign",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "GoalAmount",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "GoalName",
                table: "Accounts");

            migrationBuilder.RenameColumn(
                name: "OpenedAt",
                table: "Accounts",
                newName: "CreatedAt");
        }
    }
}
