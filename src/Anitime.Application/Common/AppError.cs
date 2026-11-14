namespace Anitime.Application.Common;

public sealed record AppError(
    string Code,
    string Message,
    int StatusCode,
    IReadOnlyDictionary<string, string[]>? Details = null);

public static class AppErrors
{
    public static AppError Validation(IReadOnlyDictionary<string, string[]> details) =>
        new("validation_failed", "One or more validation errors occurred.", 400, details);

    public static AppError Unauthorized(string message = "Authentication failed.") =>
        new("unauthorized", message, 401);

    public static AppError Forbidden(string message = "Access denied.") =>
        new("forbidden", message, 403);

    public static AppError NotFound(string message = "Resource was not found.") =>
        new("not_found", message, 404);

    public static AppError Conflict(string message) =>
        new("conflict", message, 409);

    /// <summary>An older app build tried to overwrite a note saved in a newer document format; the client must update.</summary>
    public static AppError NoteFormatTooNew() =>
        new("note_format_too_new", "This note was saved by a newer version of ANITIME. Update the app to edit it.", 409);

    public static AppError InvalidRequest(string message) =>
        new("invalid_request", message, 400);

    public static AppError PayloadTooLarge(string message) =>
        new("payload_too_large", message, 413);

    public static AppError BusinessRule(string message) =>
        new("business_rule_violation", message, 400);
}
