using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atomizer.EFCore.Example.Data.PostgresMigrations
{
    /// <inheritdoc />
    public partial class AddedErrorEntityTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AtomizerJobErrorEntity_AtomizerJobs_JobId",
                table: "AtomizerJobErrorEntity");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AtomizerJobErrorEntity",
                table: "AtomizerJobErrorEntity");

            migrationBuilder.RenameTable(
                name: "AtomizerJobErrorEntity",
                newName: "AtomizerJobErrors",
                newSchema: "Atomizer");

            migrationBuilder.RenameIndex(
                name: "IX_AtomizerJobErrorEntity_JobId",
                schema: "Atomizer",
                table: "AtomizerJobErrors",
                newName: "IX_AtomizerJobErrors_JobId");

            migrationBuilder.AlterColumn<string>(
                name: "StackTrace",
                schema: "Atomizer",
                table: "AtomizerJobErrors",
                type: "character varying(5120)",
                maxLength: 5120,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "RuntimeIdentity",
                schema: "Atomizer",
                table: "AtomizerJobErrors",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ErrorMessage",
                schema: "Atomizer",
                table: "AtomizerJobErrors",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_AtomizerJobErrors",
                schema: "Atomizer",
                table: "AtomizerJobErrors",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_AtomizerJobErrors_AtomizerJobs_JobId",
                schema: "Atomizer",
                table: "AtomizerJobErrors",
                column: "JobId",
                principalSchema: "Atomizer",
                principalTable: "AtomizerJobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AtomizerJobErrors_AtomizerJobs_JobId",
                schema: "Atomizer",
                table: "AtomizerJobErrors");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AtomizerJobErrors",
                schema: "Atomizer",
                table: "AtomizerJobErrors");

            migrationBuilder.RenameTable(
                name: "AtomizerJobErrors",
                schema: "Atomizer",
                newName: "AtomizerJobErrorEntity");

            migrationBuilder.RenameIndex(
                name: "IX_AtomizerJobErrors_JobId",
                table: "AtomizerJobErrorEntity",
                newName: "IX_AtomizerJobErrorEntity_JobId");

            migrationBuilder.AlterColumn<string>(
                name: "StackTrace",
                table: "AtomizerJobErrorEntity",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(5120)",
                oldMaxLength: 5120,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "RuntimeIdentity",
                table: "AtomizerJobErrorEntity",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ErrorMessage",
                table: "AtomizerJobErrorEntity",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1024)",
                oldMaxLength: 1024,
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_AtomizerJobErrorEntity",
                table: "AtomizerJobErrorEntity",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_AtomizerJobErrorEntity_AtomizerJobs_JobId",
                table: "AtomizerJobErrorEntity",
                column: "JobId",
                principalSchema: "Atomizer",
                principalTable: "AtomizerJobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
