using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anitime.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260422230000_InitialSchema")]
public partial class InitialSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "users",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                PasswordHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Gender = table.Column<int>(type: "integer", nullable: false),
                DateOfBirth = table.Column<DateOnly>(type: "date", nullable: true),
                ProfileImageObjectName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                ProfileImageContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                BackgroundImageObjectName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                BackgroundImageContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_users", x => x.Id));

        migrationBuilder.CreateTable(
            name: "habits",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                Emoji = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                DurationDays = table.Column<int>(type: "integer", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_habits", x => x.Id);
                table.ForeignKey("FK_habits_users_UserId", x => x.UserId, "users", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "password_reset_tokens",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                TokenHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_password_reset_tokens", x => x.Id);
                table.ForeignKey("FK_password_reset_tokens_users_UserId", x => x.UserId, "users", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "reminders",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                ScheduledForUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                RepeatDaily = table.Column<bool>(type: "boolean", nullable: false),
                IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                LastTriggeredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_reminders", x => x.Id);
                table.ForeignKey("FK_reminders_users_UserId", x => x.UserId, "users", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "tasks",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                Emoji = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                Date = table.Column<DateOnly>(type: "date", nullable: false),
                IsCompleted = table.Column<bool>(type: "boolean", nullable: false),
                CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_tasks", x => x.Id);
                table.ForeignKey("FK_tasks_users_UserId", x => x.UserId, "users", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "user_settings",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                BackgroundStyleType = table.Column<int>(type: "integer", nullable: false),
                BackgroundPrimaryColor = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                BackgroundSecondaryColor = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                AccentColor = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                Locale = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                TimeZone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_user_settings", x => x.Id);
                table.ForeignKey("FK_user_settings_users_UserId", x => x.UserId, "users", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "habit_entries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HabitId = table.Column<Guid>(type: "uuid", nullable: false),
                Date = table.Column<DateOnly>(type: "date", nullable: false),
                IsCompleted = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_habit_entries", x => x.Id);
                table.ForeignKey("FK_habit_entries_habits_HabitId", x => x.HabitId, "habits", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "checklist_items",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TaskItemId = table.Column<Guid>(type: "uuid", nullable: false),
                Title = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                IsDone = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_checklist_items", x => x.Id);
                table.ForeignKey("FK_checklist_items_tasks_TaskItemId", x => x.TaskItemId, "tasks", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(name: "IX_users_Email", table: "users", column: "Email", unique: true);
        migrationBuilder.CreateIndex(name: "IX_habits_UserId_StartDate", table: "habits", columns: new[] { "UserId", "StartDate" });
        migrationBuilder.CreateIndex(name: "IX_password_reset_tokens_TokenHash", table: "password_reset_tokens", column: "TokenHash", unique: true);
        migrationBuilder.CreateIndex(name: "IX_password_reset_tokens_UserId_ExpiresAtUtc", table: "password_reset_tokens", columns: new[] { "UserId", "ExpiresAtUtc" });
        migrationBuilder.CreateIndex(name: "IX_reminders_IsEnabled_ScheduledForUtc", table: "reminders", columns: new[] { "IsEnabled", "ScheduledForUtc" });
        migrationBuilder.CreateIndex(name: "IX_reminders_UserId_ScheduledForUtc", table: "reminders", columns: new[] { "UserId", "ScheduledForUtc" });
        migrationBuilder.CreateIndex(name: "IX_tasks_UserId_Date", table: "tasks", columns: new[] { "UserId", "Date" });
        migrationBuilder.CreateIndex(name: "IX_user_settings_UserId", table: "user_settings", column: "UserId", unique: true);
        migrationBuilder.CreateIndex(name: "IX_habit_entries_HabitId_Date", table: "habit_entries", columns: new[] { "HabitId", "Date" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_checklist_items_TaskItemId", table: "checklist_items", column: "TaskItemId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "checklist_items");
        migrationBuilder.DropTable(name: "habit_entries");
        migrationBuilder.DropTable(name: "password_reset_tokens");
        migrationBuilder.DropTable(name: "reminders");
        migrationBuilder.DropTable(name: "user_settings");
        migrationBuilder.DropTable(name: "tasks");
        migrationBuilder.DropTable(name: "habits");
        migrationBuilder.DropTable(name: "users");
    }
}
