using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atomizer.EFCore.Example.Data.Postgres.Migrations
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
                name: "AtomizerActiveServers",
                schema: "Atomizer",
                columns: table => new
                {
                    InstanceId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    LastHeartbeatAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AtomizerActiveServers", x => x.InstanceId);
                });

            migrationBuilder.CreateTable(
                name: "AtomizerJobs",
                schema: "Atomizer",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QueueKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PayloadType = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    ScheduledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VisibleAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    RetryIntervals = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseToken = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ScheduleJobKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    PartitionKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    SequenceNumber = table.Column<long>(type: "bigint", nullable: true)
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    QueueKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PayloadType = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    Schedule = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    TimeZone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MisfirePolicy = table.Column<int>(type: "integer", nullable: false),
                    MaxCatchUp = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    PartitionKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    RetryIntervals = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    NextRunAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastEnqueueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AtomizerSchedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Price = table.Column<decimal>(type: "numeric", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false)
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    StackTrace = table.Column<string>(type: "character varying(5120)", maxLength: 5120, nullable: true),
                    ExceptionType = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    RuntimeIdentity = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
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
                name: "IX_AtomizerActiveServers_LastHeartbeatAt_InstanceId",
                schema: "Atomizer",
                table: "AtomizerActiveServers",
                columns: new[] { "LastHeartbeatAt", "InstanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_AtomizerJobErrors_JobId",
                schema: "Atomizer",
                table: "AtomizerJobErrors",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_AtomizerJobs_CreatedAt",
                schema: "Atomizer",
                table: "AtomizerJobs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AtomizerJobs_IdempotencyKey",
                schema: "Atomizer",
                table: "AtomizerJobs",
                column: "IdempotencyKey");

            migrationBuilder.CreateIndex(
                name: "IX_AtomizerJobs_QueueKey_PartitionKey_SequenceNumber",
                schema: "Atomizer",
                table: "AtomizerJobs",
                columns: new[] { "QueueKey", "PartitionKey", "SequenceNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_AtomizerJobs_QueueKey_Status_Attempts_PartitionKey",
                schema: "Atomizer",
                table: "AtomizerJobs",
                columns: new[] { "QueueKey", "Status", "Attempts", "PartitionKey" });

            migrationBuilder.CreateIndex(
                name: "IX_AtomizerJobs_QueueKey_Status_ScheduledAt_Id",
                schema: "Atomizer",
                table: "AtomizerJobs",
                columns: new[] { "QueueKey", "Status", "ScheduledAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AtomizerJobs_Status_LeaseToken",
                schema: "Atomizer",
                table: "AtomizerJobs",
                columns: new[] { "Status", "LeaseToken" });

            migrationBuilder.CreateIndex(
                name: "IX_AtomizerSchedules_Enabled_NextRunAt_Id",
                schema: "Atomizer",
                table: "AtomizerSchedules",
                columns: new[] { "Enabled", "NextRunAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AtomizerSchedules_JobKey",
                schema: "Atomizer",
                table: "AtomizerSchedules",
                column: "JobKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AtomizerActiveServers",
                schema: "Atomizer");

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
