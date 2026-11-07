namespace Anitime.Contracts;

public sealed record ApiErrorResponse(
    string Code,
    string Message,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    string? TraceId = null);
