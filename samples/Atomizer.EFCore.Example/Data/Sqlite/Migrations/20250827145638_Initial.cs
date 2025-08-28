using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atomizer.EFCore.Example.Data.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "Atomizer");

            migrationBuilder.CreateTable(
                name: "AtomizerJobs",
                schema: "Atomizer",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    QueueKey = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    PayloadType = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: false),
                    ScheduledAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    VisibleAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    RetryIntervals = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    FailedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LeaseToken = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ScheduleJobKey = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AtomizerJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AtomizerSchedules",
                schema: "Atomizer",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobKey = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    QueueKey = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    PayloadType = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: false),
                    Schedule = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    TimeZone = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    MisfirePolicy = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxCatchUp = table.Column<int>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    RetryIntervals = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    NextRunAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastEnqueueAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AtomizerSchedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Price = table.Column<decimal>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AtomizerJobErrors",
                schema: "Atomizer",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    StackTrace = table.Column<string>(type: "TEXT", maxLength: 5120, nullable: true),
                    ExceptionType = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Attempt = table.Column<int>(type: "INTEGER", nullable: false),
                    RuntimeIdentity = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AtomizerJobErrors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AtomizerJobErrors_AtomizerJobs_JobId",
                        column: x => x.JobId,
                        principalSchema: "Atomizer",
                        principalTable: "AtomizerJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AtomizerJobErrors_JobId",
                schema: "Atomizer",
                table: "AtomizerJobErrors",
                column: "JobId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AtomizerJobErrors",
                schema: "Atomizer");

            migrationBuilder.DropTable(
                name: "AtomizerSchedules",
                schema: "Atomizer");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "AtomizerJobs",
                schema: "Atomizer");
        }
    }
}
