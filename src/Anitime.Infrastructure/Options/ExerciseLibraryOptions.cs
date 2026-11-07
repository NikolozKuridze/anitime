namespace Anitime.Infrastructure.Options;

public sealed class ExerciseLibraryOptions
{
    public const string SectionName = "ExerciseLibrary";

    /// <summary>
    /// Base URL prefix for exercise demonstration images. Defaults to the public-domain
    /// Free Exercise DB CDN; point this at your own MinIO/CDN once assets are mirrored.
    /// </summary>
    public string ImageBaseUrl { get; set; } =
        "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/";
}
