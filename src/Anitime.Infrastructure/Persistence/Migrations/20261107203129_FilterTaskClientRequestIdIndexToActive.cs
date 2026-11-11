using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anitime.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FilterTaskClientRequestIdIndexToActive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tasks_UserId_ClientRequestId",
                table: "tasks");

            migrationBuilder.CreateIndex(
                name: "IX_tasks_UserId_ClientRequestId",
                table: "tasks",
                columns: new[] { "UserId", "ClientRequestId" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tasks_UserId_ClientRequestId",
                table: "tasks");

            migrationBuilder.CreateIndex(
                name: "IX_tasks_UserId_ClientRequestId",
                table: "tasks",
                columns: new[] { "UserId", "ClientRequestId" },
                unique: true);
        }
    }
}
