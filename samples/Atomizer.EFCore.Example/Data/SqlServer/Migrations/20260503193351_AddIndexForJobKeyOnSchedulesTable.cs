using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atomizer.EFCore.Example.Data.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddIndexForJobKeyOnSchedulesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "QueueKey",
                schema: "Atomizer",
                table: "AtomizerSchedules",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(512)",
                oldMaxLength: 512);

            migrationBuilder.AlterColumn<string>(
                name: "JobKey",
                schema: "Atomizer",
                table: "AtomizerSchedules",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(512)",
                oldMaxLength: 512);

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
            migrationBuilder.DropIndex(
                name: "IX_AtomizerSchedules_JobKey",
                schema: "Atomizer",
                table: "AtomizerSchedules");

            migrationBuilder.AlterColumn<string>(
                name: "QueueKey",
                schema: "Atomizer",
                table: "AtomizerSchedules",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "JobKey",
                schema: "Atomizer",
                table: "AtomizerSchedules",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(255)",
                oldMaxLength: 255);
        }
    }
}
