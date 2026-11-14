using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anitime.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotesOrganisation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<Guid>>(
                name: "AssetIds",
                table: "notes",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            migrationBuilder.AddColumn<List<string>>(
                name: "ExplicitTags",
                table: "notes",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<Guid>(
                name: "FolderId",
                table: "notes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasChecklist",
                table: "notes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasImage",
                table: "notes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsLocked",
                table: "notes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<List<Guid>>(
                name: "LinkedNoteIds",
                table: "notes",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            migrationBuilder.AddColumn<string>(
                name: "PaperStyle",
                table: "notes",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "plain");

            migrationBuilder.AddColumn<string>(
                name: "SearchText",
                table: "notes",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<List<string>>(
                name: "Tags",
                table: "notes",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.CreateTable(
                name: "note_attachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    UnreferencedSinceUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_note_attachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_note_attachments_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "note_folders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_note_folders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_note_folders_note_folders_ParentId",
                        column: x => x.ParentId,
                        principalTable: "note_folders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_note_folders_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "smart_folders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FilterJson = table.Column<string>(type: "jsonb", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_smart_folders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_smart_folders_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_notes_AssetIds",
                table: "notes",
                column: "AssetIds")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_notes_FolderId",
                table: "notes",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "IX_notes_IsDeleted_DeletedAtUtc",
                table: "notes",
                columns: new[] { "IsDeleted", "DeletedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_notes_LinkedNoteIds",
                table: "notes",
                column: "LinkedNoteIds")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_notes_Tags",
                table: "notes",
                column: "Tags")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_notes_UserId_FolderId",
                table: "notes",
                columns: new[] { "UserId", "FolderId" });

            migrationBuilder.CreateIndex(
                name: "IX_notes_UserId_IsDeleted_UpdatedAtUtc",
                table: "notes",
                columns: new[] { "UserId", "IsDeleted", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_note_attachments_UnreferencedSinceUtc",
                table: "note_attachments",
                column: "UnreferencedSinceUtc");

            migrationBuilder.CreateIndex(
                name: "IX_note_attachments_UserId",
                table: "note_attachments",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_note_folders_ParentId",
                table: "note_folders",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_note_folders_UserId_ParentId",
                table: "note_folders",
                columns: new[] { "UserId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_smart_folders_UserId",
                table: "smart_folders",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_notes_note_folders_FolderId",
                table: "notes",
                column: "FolderId",
                principalTable: "note_folders",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Existing version 1 notes with a checklist, so the "has checklist" smart filter finds them before their next save.
            migrationBuilder.Sql("""
                UPDATE notes SET "HasChecklist" = true
                WHERE jsonb_path_exists("ContentJson", 'lax $.** ? (@.type == "checkboxListItem" || @.Type == "checkboxListItem")');
                """);

            // Search reads SearchText for every note it scans; lz4 decompresses several times faster than the default pglz.
            // A core column setting (PostgreSQL 14+ built with lz4, as the official images are), not an extension;
            // where lz4 isn't available the column simply keeps pglz.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    ALTER TABLE notes ALTER COLUMN "SearchText" SET COMPRESSION lz4;
                EXCEPTION WHEN feature_not_supported OR invalid_parameter_value OR syntax_error THEN
                    RAISE NOTICE 'lz4 compression is not available; SearchText keeps the default compression.';
                END
                $$;
                """);

            // Search text for existing notes (none are locked yet). Later saves recompute it in .NET (Note.BuildSearchText).
            // Plain SQL only: no extension is created or needed.
            migrationBuilder.Sql("""
                UPDATE notes SET "SearchText" = lower("Title" || E'\n' || "PlainTextPreview");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_notes_note_folders_FolderId",
                table: "notes");

            migrationBuilder.DropTable(
                name: "note_attachments");

            migrationBuilder.DropTable(
                name: "note_folders");

            migrationBuilder.DropTable(
                name: "smart_folders");

            migrationBuilder.DropIndex(
                name: "IX_notes_AssetIds",
                table: "notes");

            migrationBuilder.DropIndex(
                name: "IX_notes_FolderId",
                table: "notes");

            migrationBuilder.DropIndex(
                name: "IX_notes_IsDeleted_DeletedAtUtc",
                table: "notes");

            migrationBuilder.DropIndex(
                name: "IX_notes_LinkedNoteIds",
                table: "notes");

            migrationBuilder.DropIndex(
                name: "IX_notes_Tags",
                table: "notes");

            migrationBuilder.DropIndex(
                name: "IX_notes_UserId_FolderId",
                table: "notes");

            migrationBuilder.DropIndex(
                name: "IX_notes_UserId_IsDeleted_UpdatedAtUtc",
                table: "notes");

            migrationBuilder.DropColumn(
                name: "AssetIds",
                table: "notes");

            migrationBuilder.DropColumn(
                name: "ExplicitTags",
                table: "notes");

            migrationBuilder.DropColumn(
                name: "FolderId",
                table: "notes");

            migrationBuilder.DropColumn(
                name: "HasChecklist",
                table: "notes");

            migrationBuilder.DropColumn(
                name: "HasImage",
                table: "notes");

            migrationBuilder.DropColumn(
                name: "IsLocked",
                table: "notes");

            migrationBuilder.DropColumn(
                name: "LinkedNoteIds",
                table: "notes");

            migrationBuilder.DropColumn(
                name: "PaperStyle",
                table: "notes");

            migrationBuilder.DropColumn(
                name: "SearchText",
                table: "notes");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "notes");
        }
    }
}
