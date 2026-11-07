using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anitime.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260504124500_AddTaskClientRequestId")]
public partial class AddTaskClientRequestId : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ClientRequestId",
            table: "tasks",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_tasks_UserId_ClientRequestId",
            table: "tasks",
            columns: new[] { "UserId", "ClientRequestId" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_tasks_UserId_ClientRequestId",
            table: "tasks");

        migrationBuilder.DropColumn(
            name: "ClientRequestId",
            table: "tasks");
    }
}
