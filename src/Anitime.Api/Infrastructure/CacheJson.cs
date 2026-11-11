using System.Text.Json;
using System.Text.Json.Serialization;

namespace Anitime.Api.Infrastructure;

/// <summary>Matches MVC JSON output so a cache hit returns the same shape as a cache miss.</summary>
public static class CacheJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
}
