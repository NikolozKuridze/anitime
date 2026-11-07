using System.Text.Json;
using System.Text.Json.Serialization;
using Anitime.Domain.Entities;
using Anitime.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Anitime.Api.Infrastructure;

/// <summary>
/// Seeds curated workout collections (e.g. "Leg Day") from the bundled Data/workouts.json.
/// Idempotent: skips when the table already holds data. YouTube ids are embed-only and were
/// verified embeddable via the keyless oEmbed endpoint at curation time.
/// </summary>
public static class WorkoutCatalogSeeder
{
    private const string DataFileRelativePath = "Data/workouts.json";

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
        if (await db.Workouts.AsNoTracking().AnyAsync(cancellationToken))
        {
            return;
        }

        var filePath = Path.Combine(contentRootPath, DataFileRelativePath);
        if (!File.Exists(filePath))
        {
            logger.LogWarning("Workout catalog seed file not found at {FilePath}; skipping seed.", filePath);
            return;
        }

        await using var stream = File.OpenRead(filePath);
        var records = await JsonSerializer.DeserializeAsync<List<WorkoutSeedRecord>>(stream, SerializerOptions, cancellationToken)
            ?? [];

        var now = DateTime.UtcNow;
        var workouts = records
            .Where(record => !string.IsNullOrWhiteSpace(record.Slug) && !string.IsNullOrWhiteSpace(record.YouTubeVideoId))
            .Select(record => new Workout
            {
                Slug = record.Slug!,
                Title = record.Title!,
                Description = record.Description ?? string.Empty,
                Category = record.Category ?? "General",
                Level = record.Level ?? "beginner",
                DurationMinutes = record.DurationMinutes,
                YouTubeVideoId = record.YouTubeVideoId!,
                TargetMuscles = record.TargetMuscles ?? [],
                DisplayOrder = record.DisplayOrder,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            })
            .ToList();

        db.Workouts.AddRange(workouts);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded {Count} workout collections into the catalog.", workouts.Count);
    }

    private sealed record WorkoutSeedRecord(
        string? Slug,
        string? Title,
        string? Description,
        string? Category,
        string? Level,
        int? DurationMinutes,
        string? YouTubeVideoId,
        List<string>? TargetMuscles,
        int DisplayOrder);
}
