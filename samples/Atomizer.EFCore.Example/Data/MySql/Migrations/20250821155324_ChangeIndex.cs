using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atomizer.EFCore.Example.Data.MySql.Migrations
{
    /// <inheritdoc />
    public partial class ChangeIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AtomizerJobs_IdempotencyKey",
                table: "AtomizerJobs");

            migrationBuilder.RenameColumn(
                name: "idempotency_key",
                table: "AtomizerJobs",
                newName: "IdempotencyKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "IdempotencyKey",
                table: "AtomizerJobs",
                newName: "idempotency_key");

            migrationBuilder.CreateIndex(
                name: "IX_AtomizerJobs_IdempotencyKey",
                table: "AtomizerJobs",
                column: "idempotency_key",
                unique: true,
                filter: "idempotency_key IS NOT NULL");
        }
    }
}
