using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anitime.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260523104500_IncreaseNotePlainTextPreviewLimit")]
public partial class IncreaseNotePlainTextPreviewLimit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "PlainTextPreview",
            table: "notes",
            type: "character varying(50000)",
            maxLength: 50000,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(1000)",
            oldMaxLength: 1000);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "PlainTextPreview",
            table: "notes",
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(50000)",
            oldMaxLength: 50000);
    }
}
