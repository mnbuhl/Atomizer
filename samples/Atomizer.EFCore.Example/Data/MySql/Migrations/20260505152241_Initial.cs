using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atomizer.EFCore.Example.Data.MySql.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase().Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "AtomizerActiveServers",
                    columns: table => new
                    {
                        InstanceId = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        LastHeartbeatAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_AtomizerActiveServers", x => x.InstanceId);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "AtomizerJobs",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                        QueueKey = table
                            .Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        PayloadType = table
                            .Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        Payload = table
                            .Column<string>(type: "longtext", nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        ScheduledAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                        VisibleAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                        Status = table.Column<int>(type: "int", nullable: false),
                        Attempts = table.Column<int>(type: "int", nullable: false),
                        RetryIntervals = table
                            .Column<string>(type: "varchar(4096)", maxLength: 4096, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                        UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                        CompletedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                        FailedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                        LeaseToken = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        ScheduleJobKey = table
                            .Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        IdempotencyKey = table
                            .Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        PartitionKey = table
                            .Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        SequenceNumber = table.Column<long>(type: "bigint", nullable: true),
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_AtomizerJobs", x => x.Id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "AtomizerSchedules",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                        JobKey = table
                            .Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        QueueKey = table
                            .Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        PayloadType = table
                            .Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        Payload = table
                            .Column<string>(type: "longtext", nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        Schedule = table
                            .Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        TimeZone = table
                            .Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        MisfirePolicy = table.Column<int>(type: "int", nullable: false),
                        MaxCatchUp = table.Column<int>(type: "int", nullable: false),
                        Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                        PartitionKey = table
                            .Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        RetryIntervals = table
                            .Column<string>(type: "varchar(4096)", maxLength: 4096, nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        NextRunAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                        LastEnqueueAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                        CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                        UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_AtomizerSchedules", x => x.Id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "Products",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                        Name = table
                            .Column<string>(type: "longtext", nullable: false)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        Price = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                        CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                        Quantity = table.Column<int>(type: "int", nullable: false),
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_Products", x => x.Id);
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "AtomizerJobErrors",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                        JobId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                        ErrorMessage = table
                            .Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        StackTrace = table
                            .Column<string>(type: "varchar(5120)", maxLength: 5120, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        ExceptionType = table
                            .Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                        CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                        Attempt = table.Column<int>(type: "int", nullable: false),
                        RuntimeIdentity = table
                            .Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                            .Annotation("MySql:CharSet", "utf8mb4"),
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_AtomizerJobErrors", x => x.Id);
                        table.ForeignKey(
                            name: "FK_AtomizerJobErrors_AtomizerJobs_JobId",
                            column: x => x.JobId,
                            principalTable: "AtomizerJobs",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AtomizerActiveServers_LastHeartbeatAt_InstanceId",
                table: "AtomizerActiveServers",
                columns: new[] { "LastHeartbeatAt", "InstanceId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_AtomizerJobErrors_JobId",
                table: "AtomizerJobErrors",
                column: "JobId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AtomizerJobs_IdempotencyKey",
                table: "AtomizerJobs",
                column: "IdempotencyKey"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AtomizerJobs_QueueKey_PartitionKey_SequenceNumber",
                table: "AtomizerJobs",
                columns: new[] { "QueueKey", "PartitionKey", "SequenceNumber" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_AtomizerJobs_QueueKey_Status_Attempts_PartitionKey",
                table: "AtomizerJobs",
                columns: new[] { "QueueKey", "Status", "Attempts", "PartitionKey" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_AtomizerJobs_QueueKey_Status_ScheduledAt_Id",
                table: "AtomizerJobs",
                columns: new[] { "QueueKey", "Status", "ScheduledAt", "Id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_AtomizerJobs_Status_LeaseToken",
                table: "AtomizerJobs",
                columns: new[] { "Status", "LeaseToken" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_AtomizerSchedules_Enabled_NextRunAt_Id",
                table: "AtomizerSchedules",
                columns: new[] { "Enabled", "NextRunAt", "Id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_AtomizerSchedules_JobKey",
                table: "AtomizerSchedules",
                column: "JobKey",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AtomizerActiveServers");

            migrationBuilder.DropTable(name: "AtomizerJobErrors");

            migrationBuilder.DropTable(name: "AtomizerSchedules");

            migrationBuilder.DropTable(name: "Products");

            migrationBuilder.DropTable(name: "AtomizerJobs");
        }
    }
}
