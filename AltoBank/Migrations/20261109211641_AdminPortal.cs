using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltoBank.Migrations
{
    /// <inheritdoc />
    public partial class AdminPortal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AccountApplications_Accounts_AccountId",
                table: "AccountApplications");

            migrationBuilder.DropForeignKey(
                name: "FK_AccountApplications_AspNetUsers_UserId",
                table: "AccountApplications");

            migrationBuilder.DropForeignKey(
                name: "FK_Accounts_AspNetUsers_UserId",
                table: "Accounts");

            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Accounts_AccountId",
                table: "Transactions");

            migrationBuilder.AddColumn<int>(
                name: "CounterpartyAccountId",
                table: "Transactions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LinkedTransactionId",
                table: "Transactions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Memo",
                table: "Transactions",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReversalOfTransactionId",
                table: "Transactions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReversedByTransactionId",
                table: "Transactions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Transactions",
                type: "TEXT",
                maxLength: 10,
                nullable: false,
                defaultValue: "Posted");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedAt",
                table: "Accounts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DailyTransferLimit",
                table: "Accounts",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Accounts",
                type: "TEXT",
                maxLength: 10,
                nullable: false,
                defaultValue: "Active");

            migrationBuilder.AddColumn<DateTime>(
                name: "StatusChangedAt",
                table: "Accounts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StatusReason",
                table: "Accounts",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CardDesign",
                table: "AccountApplications",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DenialReason",
                table: "AccountApplications",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FundingAccountId",
                table: "AccountApplications",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "AccountApplications",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedBy",
                table: "AccountApplications",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ActorUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    ActorEmail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ActorRole = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    TargetType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    TargetId = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    BeforeJson = table.Column<string>(type: "TEXT", nullable: true),
                    AfterJson = table.Column<string>(type: "TEXT", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IpAddress = table.Column<string>(type: "TEXT", maxLength: 45, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BankSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SavingsApy = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    CheckingApy = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    DefaultDailyTransferLimit = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LargeTransactionThreshold = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AutoApproveApplications = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Cards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AccountId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    CardNumber = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ExpiryMonth = table.Column<int>(type: "INTEGER", nullable: false),
                    ExpiryYear = table.Column<int>(type: "INTEGER", nullable: false),
                    NameOnCard = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Design = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    IssuedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FrozenAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CancelReason = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    ReplacesCardId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cards_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CustomerUserId = table.Column<string>(type: "TEXT", nullable: false),
                    AuthorUserId = table.Column<string>(type: "TEXT", nullable: false),
                    AuthorName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerNotes_AspNetUsers_CustomerUserId",
                        column: x => x.CustomerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "BankSettings",
                columns: new[] { "Id", "AutoApproveApplications", "CheckingApy", "DefaultDailyTransferLimit", "LargeTransactionThreshold", "SavingsApy", "UpdatedAt", "UpdatedBy" },
                values: new object[] { 1, true, 0.0000m, 5000m, 2500m, 0.0410m, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_Date",
                table: "Transactions",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_AccountApplications_Status",
                table: "AccountApplications",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Action",
                table: "AuditLogs",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_ActorUserId",
                table: "AuditLogs",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Timestamp",
                table: "AuditLogs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_AccountId",
                table: "Cards",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_CardNumber",
                table: "Cards",
                column: "CardNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerNotes_CustomerUserId_CreatedAt",
                table: "CustomerNotes",
                columns: new[] { "CustomerUserId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_AccountApplications_Accounts_AccountId",
                table: "AccountApplications",
                column: "AccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AccountApplications_AspNetUsers_UserId",
                table: "AccountApplications",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Accounts_AspNetUsers_UserId",
                table: "Accounts",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Accounts_AccountId",
                table: "Transactions",
                column: "AccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // ----- Hand-written data fixes so existing rows match the new model -----

            // ApplicationStatus "Declined" was renamed to "Denied": convert old rows so they still load.
            migrationBuilder.Sql("UPDATE AccountApplications SET Status = 'Denied' WHERE Status = 'Declined';");

            // Interest used to be recorded as a plain Deposit named "Interest earned"; it now has its own type.
            migrationBuilder.Sql("UPDATE Transactions SET Type = 'Interest' WHERE Type = 'Deposit' AND Description = 'Interest earned';");

            // New CreatedAt ("joined") column: use the date of each customer's first account.
            migrationBuilder.Sql(@"UPDATE AspNetUsers
                                   SET CreatedAt = (SELECT MIN(a.OpenedAt) FROM Accounts a WHERE a.UserId = AspNetUsers.Id)
                                   WHERE CreatedAt IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Undo the data fixes first (while the columns still exist).
            migrationBuilder.Sql("UPDATE AccountApplications SET Status = 'Declined' WHERE Status = 'Denied';");
            migrationBuilder.Sql("UPDATE Transactions SET Type = 'Deposit' WHERE Type = 'Interest';");

            migrationBuilder.DropForeignKey(
                name: "FK_AccountApplications_Accounts_AccountId",
                table: "AccountApplications");

            migrationBuilder.DropForeignKey(
                name: "FK_AccountApplications_AspNetUsers_UserId",
                table: "AccountApplications");

            migrationBuilder.DropForeignKey(
                name: "FK_Accounts_AspNetUsers_UserId",
                table: "Accounts");

            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Accounts_AccountId",
                table: "Transactions");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "BankSettings");

            migrationBuilder.DropTable(
                name: "Cards");

            migrationBuilder.DropTable(
                name: "CustomerNotes");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_Date",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_AccountApplications_Status",
                table: "AccountApplications");

            migrationBuilder.DropColumn(
                name: "CounterpartyAccountId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "LinkedTransactionId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "Memo",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ReversalOfTransactionId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ReversedByTransactionId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ClosedAt",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "DailyTransferLimit",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "StatusChangedAt",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "StatusReason",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "CardDesign",
                table: "AccountApplications");

            migrationBuilder.DropColumn(
                name: "DenialReason",
                table: "AccountApplications");

            migrationBuilder.DropColumn(
                name: "FundingAccountId",
                table: "AccountApplications");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "AccountApplications");

            migrationBuilder.DropColumn(
                name: "ReviewedBy",
                table: "AccountApplications");

            migrationBuilder.AddForeignKey(
                name: "FK_AccountApplications_Accounts_AccountId",
                table: "AccountApplications",
                column: "AccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_AccountApplications_AspNetUsers_UserId",
                table: "AccountApplications",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Accounts_AspNetUsers_UserId",
                table: "Accounts",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Accounts_AccountId",
                table: "Transactions",
                column: "AccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
