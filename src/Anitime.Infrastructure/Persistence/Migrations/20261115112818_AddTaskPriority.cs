using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anitime.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskPriority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Priority",
                table: "tasks",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                // Existing tasks (and rows inserted by older code paths) become Medium, the app's default.
                defaultValue: "Medium");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Priority",
                table: "tasks");
        }
    }
}
