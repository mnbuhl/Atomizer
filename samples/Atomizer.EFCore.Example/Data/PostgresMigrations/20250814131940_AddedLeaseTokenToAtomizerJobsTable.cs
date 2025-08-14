using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atomizer.EFCore.Example.Data.PostgresMigrations
{
    /// <inheritdoc />
    public partial class AddedLeaseTokenToAtomizerJobsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LeaseToken",
                schema: "Atomizer",
                table: "AtomizerJobs",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LeaseToken",
                schema: "Atomizer",
                table: "AtomizerJobs");
        }
    }
}
