using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atomizer.EntityFrameworkCore.Tests.TestSetup.Sqlite.Migrations
{
    public partial class Initial : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AtomizerJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    QueueKey = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    PayloadType = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: false),
                    ScheduledAt = table.Column<long>(type: "INTEGER", nullable: false),
                    VisibleAt = table.Column<long>(type: "INTEGER", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    RetryIntervals = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CompletedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    FailedAt = table.Column<long>(type: "INTEGER", nullable: true),
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
                    NextRunAt = table.Column<long>(type: "INTEGER", nullable: false),
                    LastEnqueueAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AtomizerSchedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AtomizerJobErrors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    StackTrace = table.Column<string>(type: "TEXT", maxLength: 5120, nullable: true),
                    ExceptionType = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Attempt = table.Column<int>(type: "INTEGER", nullable: false),
                    RuntimeIdentity = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AtomizerJobErrors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AtomizerJobErrors_AtomizerJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "AtomizerJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AtomizerJobErrors_JobId",
                table: "AtomizerJobErrors",
                column: "JobId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AtomizerJobErrors");

            migrationBuilder.DropTable(
                name: "AtomizerSchedules");

            migrationBuilder.DropTable(
                name: "AtomizerJobs");
        }
    }
}
