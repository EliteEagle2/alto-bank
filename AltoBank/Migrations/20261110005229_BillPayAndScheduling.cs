using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltoBank.Migrations
{
    /// <inheritdoc />
    public partial class BillPayAndScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SchedulerLastRunAt",
                table: "BankSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SchedulerLastRunSummary",
                table: "BankSettings",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Payees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    AccountNumber = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Nickname = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false, defaultValue: "Active"),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RemovedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payees_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SchedulerRunLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Trigger = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    TriggeredBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Processed = table.Column<int>(type: "INTEGER", nullable: false),
                    Succeeded = table.Column<int>(type: "INTEGER", nullable: false),
                    Failed = table.Column<int>(type: "INTEGER", nullable: false),
                    Skipped = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestsExpired = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchedulerRunLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScheduledPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    FromAccountId = table.Column<int>(type: "INTEGER", nullable: false),
                    PayeeId = table.Column<int>(type: "INTEGER", nullable: true),
                    ToAccountId = table.Column<int>(type: "INTEGER", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Memo = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    Frequency = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    DayOfWeek = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    DayOfMonth = table.Column<int>(type: "INTEGER", nullable: true),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    NextRunDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    SkipNext = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false, defaultValue: "Active"),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CancelledAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastRunAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastRunStatus = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    LastFailureReason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduledPayments_Accounts_FromAccountId",
                        column: x => x.FromAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduledPayments_Accounts_ToAccountId",
                        column: x => x.ToAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduledPayments_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduledPayments_Payees_PayeeId",
                        column: x => x.PayeeId,
                        principalTable: "Payees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ScheduledRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ScheduledPaymentId = table.Column<int>(type: "INTEGER", nullable: false),
                    RunDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    FailureReason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TransactionId = table.Column<int>(type: "INTEGER", nullable: true),
                    ProcessedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduledRuns_ScheduledPayments_ScheduledPaymentId",
                        column: x => x.ScheduledPaymentId,
                        principalTable: "ScheduledPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "BankSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "SchedulerLastRunAt", "SchedulerLastRunSummary" },
                values: new object[] { null, null });

            migrationBuilder.CreateIndex(
                name: "IX_Payees_UserId_Status",
                table: "Payees",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledPayments_FromAccountId",
                table: "ScheduledPayments",
                column: "FromAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledPayments_PayeeId",
                table: "ScheduledPayments",
                column: "PayeeId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledPayments_Status_NextRunDate",
                table: "ScheduledPayments",
                columns: new[] { "Status", "NextRunDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledPayments_ToAccountId",
                table: "ScheduledPayments",
                column: "ToAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledPayments_UserId_Status",
                table: "ScheduledPayments",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledRuns_ScheduledPaymentId_RunDate",
                table: "ScheduledRuns",
                columns: new[] { "ScheduledPaymentId", "RunDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledRuns_Status_ProcessedAt",
                table: "ScheduledRuns",
                columns: new[] { "Status", "ProcessedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SchedulerRunLogs_StartedAt",
                table: "SchedulerRunLogs",
                column: "StartedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScheduledRuns");

            migrationBuilder.DropTable(
                name: "SchedulerRunLogs");

            migrationBuilder.DropTable(
                name: "ScheduledPayments");

            migrationBuilder.DropTable(
                name: "Payees");

            migrationBuilder.DropColumn(
                name: "SchedulerLastRunAt",
                table: "BankSettings");

            migrationBuilder.DropColumn(
                name: "SchedulerLastRunSummary",
                table: "BankSettings");
        }
    }
}
