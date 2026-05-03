using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atomizer.EFCore.Example.Data.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddIndexForJobKeyOnSchedulesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }
    }
}
