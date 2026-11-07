namespace Anitime.Application.Caching;

public static class CacheKeys
{
    public static string Dashboard(Guid userId) => $"dashboard:{userId}";
    public static string Profile(Guid userId) => $"profile:{userId}";
    public const string ExerciseFilters = "exercises:filters";
}
