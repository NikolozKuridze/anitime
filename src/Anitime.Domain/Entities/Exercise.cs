namespace Anitime.Domain.Entities;

/// <summary>
/// Reference catalog entry sourced from the public-domain Free Exercise DB
/// (https://github.com/yuhonas/free-exercise-db, Unlicense). Global, not user-owned.
/// </summary>
public sealed class Exercise : BaseEntity
{
    /// <summary>Stable source identifier, e.g. "Barbell_Bench_Press_-_Medium_Grip".</summary>
    public string Slug { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string Category { get; set; } = default!;
    public string Level { get; set; } = default!;
    public string? Force { get; set; }
    public string? Mechanic { get; set; }
    public string? Equipment { get; set; }

    public List<string> PrimaryMuscles { get; set; } = [];
    public List<string> SecondaryMuscles { get; set; } = [];
    public List<string> Instructions { get; set; } = [];

    /// <summary>Relative image paths (e.g. "Barbell_Bench_Press_-_Medium_Grip/0.jpg"); absolute URLs built at the API boundary.</summary>
    public List<string> ImagePaths { get; set; } = [];

    /// <summary>Optional YouTube video id for an embeddable demonstration clip. Embed URL built at the API boundary.</summary>
    public string? YouTubeVideoId { get; set; }
}
