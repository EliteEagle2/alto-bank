using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltoBank.Migrations
{
    /// <inheritdoc />
    public partial class ProfileSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserPreferences",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    PreferredName = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    DefaultAccountId = table.Column<int>(type: "INTEGER", nullable: true),
                    HideBalances = table.Column<bool>(type: "INTEGER", nullable: false),
                    Theme = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    Paperless = table.Column<bool>(type: "INTEGER", nullable: false),
                    DiscoverableByPhone = table.Column<bool>(type: "INTEGER", nullable: false),
                    LowBalanceAlert = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    LargeDebitAlert = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    MutedAlerts = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPreferences", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserPreferences_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserPreferences");
        }
    }
}
