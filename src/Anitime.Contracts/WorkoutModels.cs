namespace Anitime.Contracts;

public sealed record WorkoutSummaryResponse(
    Guid Id,
    string Slug,
    string Title,
    string Description,
    string Category,
    string Level,
    int? DurationMinutes,
    string YouTubeVideoId,
    string VideoEmbedUrl,
    string ThumbnailUrl,
    IReadOnlyList<string> TargetMuscles);

public sealed record WorkoutDetailResponse(
    Guid Id,
    string Slug,
    string Title,
    string Description,
    string Category,
    string Level,
    int? DurationMinutes,
    string YouTubeVideoId,
    string VideoEmbedUrl,
    string ThumbnailUrl,
    IReadOnlyList<string> TargetMuscles,
    IReadOnlyList<ExerciseSummaryResponse> Exercises);
