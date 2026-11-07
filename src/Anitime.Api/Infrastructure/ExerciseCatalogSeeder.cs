using System.Text.Json;
using System.Text.Json.Serialization;
using Anitime.Domain.Entities;
using Anitime.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Anitime.Api.Infrastructure;

/// <summary>
/// Seeds the exercise catalog once from the bundled public-domain Free Exercise DB
/// snapshot (Data/exercises.json). Idempotent: skips when the table already holds data.
/// </summary>
public static class ExerciseCatalogSeeder
{
    private const string DataFileRelativePath = "Data/exercises.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static async Task SeedAsync(
        AppDbContext db,
        string contentRootPath,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (await db.Exercises.AsNoTracking().AnyAsync(cancellationToken))
        {
            return;
        }

        var filePath = Path.Combine(contentRootPath, DataFileRelativePath);
        if (!File.Exists(filePath))
        {
            logger.LogWarning("Exercise catalog seed file not found at {FilePath}; skipping seed.", filePath);
            return;
        }

        await using var stream = File.OpenRead(filePath);
        var records = await JsonSerializer.DeserializeAsync<List<ExerciseSeedRecord>>(stream, SerializerOptions, cancellationToken)
            ?? [];

        var now = DateTime.UtcNow;
        var exercises = records
            .Where(record => !string.IsNullOrWhiteSpace(record.Id) && !string.IsNullOrWhiteSpace(record.Name))
            .Select(record => new Exercise
            {
                Slug = record.Id!,
                Name = record.Name!,
                Category = record.Category ?? "other",
                Level = record.Level ?? "beginner",
                Force = record.Force,
                Mechanic = record.Mechanic,
                Equipment = record.Equipment,
                PrimaryMuscles = record.PrimaryMuscles ?? [],
                SecondaryMuscles = record.SecondaryMuscles ?? [],
                Instructions = record.Instructions ?? [],
                ImagePaths = record.Images ?? [],
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            })
            .ToList();

        db.Exercises.AddRange(exercises);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded {Count} exercises into the catalog.", exercises.Count);
    }

    private sealed record ExerciseSeedRecord(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("category")] string? Category,
        [property: JsonPropertyName("level")] string? Level,
        [property: JsonPropertyName("force")] string? Force,
        [property: JsonPropertyName("mechanic")] string? Mechanic,
        [property: JsonPropertyName("equipment")] string? Equipment,
        [property: JsonPropertyName("primaryMuscles")] List<string>? PrimaryMuscles,
        [property: JsonPropertyName("secondaryMuscles")] List<string>? SecondaryMuscles,
        [property: JsonPropertyName("instructions")] List<string>? Instructions,
        [property: JsonPropertyName("images")] List<string>? Images);
}
