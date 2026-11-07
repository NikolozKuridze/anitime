namespace Anitime.Contracts;

public sealed record ExerciseSummaryResponse(
    Guid Id,
    string Slug,
    string Name,
    string Category,
    string Level,
    string? Equipment,
    IReadOnlyList<string> PrimaryMuscles,
    string? PrimaryImageUrl);

public sealed record ExerciseDetailResponse(
    Guid Id,
    string Slug,
    string Name,
    string Category,
    string Level,
    string? Force,
    string? Mechanic,
    string? Equipment,
    IReadOnlyList<string> PrimaryMuscles,
    IReadOnlyList<string> SecondaryMuscles,
    IReadOnlyList<string> Instructions,
    IReadOnlyList<string> ImageUrls,
    string? VideoEmbedUrl);

public sealed record ExerciseListResponse(
    IReadOnlyList<ExerciseSummaryResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record ExerciseFiltersResponse(
    IReadOnlyList<string> Muscles,
    IReadOnlyList<string> Equipment,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Levels);
