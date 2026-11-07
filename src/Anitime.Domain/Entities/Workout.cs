namespace Anitime.Domain.Entities;

/// <summary>
/// Curated workout collection (e.g. "Leg Day", "Push Day") mapping a full-length
/// embeddable YouTube routine to a set of target muscles. Matching exercises are
/// resolved from the <see cref="Exercise"/> catalog by those muscles at read time.
/// </summary>
public sealed class Workout : BaseEntity
{
    public string Slug { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string Description { get; set; } = default!;

    /// <summary>Grouping shown in the UI, e.g. "Lower Body", "Upper Body", "Core", "Cardio", "Mobility".</summary>
    public string Category { get; set; } = default!;
    public string Level { get; set; } = "beginner";
    public int? DurationMinutes { get; set; }

    /// <summary>Embeddable YouTube video id for the full routine; embed URL built at the API boundary.</summary>
    public string YouTubeVideoId { get; set; } = default!;

    /// <summary>Muscle keys (matching the exercise catalog) used to surface related exercises.</summary>
    public List<string> TargetMuscles { get; set; } = [];

    /// <summary>Lower numbers sort first.</summary>
    public int DisplayOrder { get; set; }
}
