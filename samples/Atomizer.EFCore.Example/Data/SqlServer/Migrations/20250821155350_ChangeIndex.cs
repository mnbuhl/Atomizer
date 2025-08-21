using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atomizer.EFCore.Example.Data.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class ChangeIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AtomizerJobs_IdempotencyKey",
                schema: "Atomizer",
                table: "AtomizerJobs");

            migrationBuilder.RenameColumn(
                name: "idempotency_key",
                schema: "Atomizer",
                table: "AtomizerJobs",
                newName: "IdempotencyKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "IdempotencyKey",
                schema: "Atomizer",
                table: "AtomizerJobs",
                newName: "idempotency_key");

            migrationBuilder.CreateIndex(
                name: "IX_AtomizerJobs_IdempotencyKey",
                schema: "Atomizer",
                table: "AtomizerJobs",
                column: "idempotency_key",
                unique: true,
                filter: "idempotency_key IS NOT NULL");
        }
    }
}
