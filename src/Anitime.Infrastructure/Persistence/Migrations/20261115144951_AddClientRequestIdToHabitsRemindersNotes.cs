using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anitime.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClientRequestIdToHabitsRemindersNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClientRequestId",
                table: "reminders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClientRequestId",
                table: "notes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClientRequestId",
                table: "habits",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_reminders_UserId_ClientRequestId",
                table: "reminders",
                columns: new[] { "UserId", "ClientRequestId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_notes_UserId_ClientRequestId",
                table: "notes",
                columns: new[] { "UserId", "ClientRequestId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_habits_UserId_ClientRequestId",
                table: "habits",
                columns: new[] { "UserId", "ClientRequestId" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_reminders_UserId_ClientRequestId",
                table: "reminders");

            migrationBuilder.DropIndex(
                name: "IX_notes_UserId_ClientRequestId",
                table: "notes");

            migrationBuilder.DropIndex(
                name: "IX_habits_UserId_ClientRequestId",
                table: "habits");

            migrationBuilder.DropColumn(
                name: "ClientRequestId",
                table: "reminders");

            migrationBuilder.DropColumn(
                name: "ClientRequestId",
                table: "notes");

            migrationBuilder.DropColumn(
                name: "ClientRequestId",
                table: "habits");
        }
    }
}
