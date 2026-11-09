using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltoBank.Migrations
{
    /// <inheritdoc />
    public partial class AccountApplication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Apt",
                table: "AspNetUsers",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "AspNetUsers",
                type: "TEXT",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DateOfBirth",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MiddleInitial",
                table: "AspNetUsers",
                type: "TEXT",
                maxLength: 1,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "AspNetUsers",
                type: "TEXT",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Street",
                table: "AspNetUsers",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Zip",
                table: "AspNetUsers",
                type: "TEXT",
                maxLength: 5,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AccountApplications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    AccountId = table.Column<int>(type: "INTEGER", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AccountType = table.Column<string>(type: "TEXT", nullable: false),
                    Nickname = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    FirstName = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    MiddleInitial = table.Column<string>(type: "TEXT", maxLength: 1, nullable: true),
                    LastName = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    DateOfBirth = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Citizenship = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Street = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Apt = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    City = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false),
                    Zip = table.Column<string>(type: "TEXT", maxLength: 5, nullable: false),
                    LivedAtAddressTwoYears = table.Column<bool>(type: "INTEGER", nullable: false),
                    PrevStreet = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    PrevCity = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    PrevState = table.Column<string>(type: "TEXT", maxLength: 2, nullable: true),
                    PrevZip = table.Column<string>(type: "TEXT", maxLength: 5, nullable: true),
                    EmploymentStatus = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Employer = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    Occupation = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    IncomeRange = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    SourceOfFunds = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SsnLast4 = table.Column<string>(type: "TEXT", maxLength: 4, nullable: false),
                    OpeningDeposit = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    FundingSource = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ESignName = table.Column<string>(type: "TEXT", maxLength: 110, nullable: false),
                    ESignedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountApplications_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AccountApplications_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountApplications_AccountId",
                table: "AccountApplications",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountApplications_UserId_SubmittedAt",
                table: "AccountApplications",
                columns: new[] { "UserId", "SubmittedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountApplications");

            migrationBuilder.DropColumn(
                name: "Apt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "City",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "DateOfBirth",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "MiddleInitial",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "State",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Street",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Zip",
                table: "AspNetUsers");
        }
    }
}
