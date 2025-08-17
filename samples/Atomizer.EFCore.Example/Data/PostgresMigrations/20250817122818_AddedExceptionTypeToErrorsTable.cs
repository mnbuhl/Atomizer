using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atomizer.EFCore.Example.Data.PostgresMigrations
{
    /// <inheritdoc />
    public partial class AddedExceptionTypeToErrorsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExceptionType",
                schema: "Atomizer",
                table: "AtomizerJobErrors",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExceptionType",
                schema: "Atomizer",
                table: "AtomizerJobErrors");
        }
    }
}
