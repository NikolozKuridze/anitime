using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anitime.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260523103000_AddSoftDelete")]
public partial class AddSoftDelete : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        AddSoftDeleteColumns(migrationBuilder, "users");
        AddSoftDeleteColumns(migrationBuilder, "user_settings");
        AddSoftDeleteColumns(migrationBuilder, "habits");
        AddSoftDeleteColumns(migrationBuilder, "habit_entries");
        AddSoftDeleteColumns(migrationBuilder, "tasks");
        AddSoftDeleteColumns(migrationBuilder, "notes");
        AddSoftDeleteColumns(migrationBuilder, "checklist_items");
        AddSoftDeleteColumns(migrationBuilder, "reminders");
        AddSoftDeleteColumns(migrationBuilder, "password_reset_tokens");
        AddSoftDeleteColumns(migrationBuilder, "refresh_tokens");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        DropSoftDeleteColumns(migrationBuilder, "refresh_tokens");
        DropSoftDeleteColumns(migrationBuilder, "password_reset_tokens");
        DropSoftDeleteColumns(migrationBuilder, "reminders");
        DropSoftDeleteColumns(migrationBuilder, "checklist_items");
        DropSoftDeleteColumns(migrationBuilder, "notes");
        DropSoftDeleteColumns(migrationBuilder, "tasks");
        DropSoftDeleteColumns(migrationBuilder, "habit_entries");
        DropSoftDeleteColumns(migrationBuilder, "habits");
        DropSoftDeleteColumns(migrationBuilder, "user_settings");
        DropSoftDeleteColumns(migrationBuilder, "users");
    }

    private static void AddSoftDeleteColumns(MigrationBuilder migrationBuilder, string tableName)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsDeleted",
            table: tableName,
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<DateTime>(
            name: "DeletedAtUtc",
            table: tableName,
            type: "timestamp with time zone",
            nullable: true);
    }

    private static void DropSoftDeleteColumns(MigrationBuilder migrationBuilder, string tableName)
    {
        migrationBuilder.DropColumn(name: "DeletedAtUtc", table: tableName);
        migrationBuilder.DropColumn(name: "IsDeleted", table: tableName);
    }
}
