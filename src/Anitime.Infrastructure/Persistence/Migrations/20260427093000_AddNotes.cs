using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anitime.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260427093000_AddNotes")]
public partial class AddNotes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "notes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                Content = table.Column<string>(type: "character varying(50000)", maxLength: 50000, nullable: false),
                ContentFormat = table.Column<int>(type: "integer", nullable: false),
                FontFamily = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                FontSize = table.Column<int>(type: "integer", nullable: false),
                FontWeight = table.Column<int>(type: "integer", nullable: false),
                TextColor = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                BackgroundColor = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                TextAlign = table.Column<int>(type: "integer", nullable: false),
                IsPinned = table.Column<bool>(type: "boolean", nullable: false),
                IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                LastEditedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_notes", x => x.Id);
                table.ForeignKey(
                    name: "FK_notes_users_UserId",
                    column: x => x.UserId,
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_notes_UserId_IsArchived_UpdatedAtUtc",
            table: "notes",
            columns: new[] { "UserId", "IsArchived", "UpdatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_notes_UserId_IsPinned_UpdatedAtUtc",
            table: "notes",
            columns: new[] { "UserId", "IsPinned", "UpdatedAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "notes");
    }
}
