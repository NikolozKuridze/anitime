using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anitime.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExercises : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "exercises",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Level = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Force = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Mechanic = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Equipment = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    PrimaryMuscles = table.Column<List<string>>(type: "text[]", nullable: false),
                    SecondaryMuscles = table.Column<List<string>>(type: "text[]", nullable: false),
                    Instructions = table.Column<List<string>>(type: "text[]", nullable: false),
                    ImagePaths = table.Column<List<string>>(type: "text[]", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exercises", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_exercises_Category",
                table: "exercises",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_exercises_Equipment",
                table: "exercises",
                column: "Equipment");

            migrationBuilder.CreateIndex(
                name: "IX_exercises_Level",
                table: "exercises",
                column: "Level");

            migrationBuilder.CreateIndex(
                name: "IX_exercises_PrimaryMuscles",
                table: "exercises",
                column: "PrimaryMuscles")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_exercises_Slug",
                table: "exercises",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exercises");
        }
    }
}
