using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anitime.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260427114500_UpgradeNotesToRichText")]
public partial class UpgradeNotesToRichText : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ContentJson",
            table: "notes",
            type: "jsonb",
            nullable: false,
            defaultValue: "{\"version\":1,\"blocks\":[]}");

        migrationBuilder.AddColumn<string>(
            name: "PlainTextPreview",
            table: "notes",
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "VersionToken",
            table: "notes",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "seed-version-token");

        migrationBuilder.Sql("""
            UPDATE notes
            SET "ContentJson" = jsonb_build_object(
                    'version', 1,
                    'blocks', CASE
                        WHEN COALESCE("Content", '') = '' THEN '[]'::jsonb
                        ELSE jsonb_build_array(
                            jsonb_build_object(
                                'type', 'paragraph',
                                'align', 'left',
                                'children', jsonb_build_array(
                                    jsonb_build_object(
                                        'textNode',
                                        jsonb_build_object(
                                            'type', 'text',
                                            'text', "Content"
                                        )
                                    )
                                )
                            )
                        )
                    END
                ),
                "PlainTextPreview" = left(COALESCE("Content", ''), 1000),
                "VersionToken" = md5(random()::text || clock_timestamp()::text || "Id"::text);
            """);

        migrationBuilder.DropColumn(
            name: "Content",
            table: "notes");

        migrationBuilder.DropColumn(
            name: "ContentFormat",
            table: "notes");

        migrationBuilder.DropColumn(
            name: "TextAlign",
            table: "notes");

        migrationBuilder.CreateIndex(
            name: "IX_notes_UserId_VersionToken",
            table: "notes",
            columns: new[] { "UserId", "VersionToken" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_notes_UserId_VersionToken",
            table: "notes");

        migrationBuilder.AddColumn<string>(
            name: "Content",
            table: "notes",
            type: "character varying(50000)",
            maxLength: 50000,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<int>(
            name: "ContentFormat",
            table: "notes",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "TextAlign",
            table: "notes",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.Sql("""
            UPDATE notes
            SET "Content" = COALESCE("PlainTextPreview", ''),
                "ContentFormat" = 0,
                "TextAlign" = 0;
            """);

        migrationBuilder.DropColumn(name: "ContentJson", table: "notes");
        migrationBuilder.DropColumn(name: "PlainTextPreview", table: "notes");
        migrationBuilder.DropColumn(name: "VersionToken", table: "notes");
    }
}
