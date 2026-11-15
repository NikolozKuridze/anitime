namespace Anitime.Contracts;

public sealed record CreateReminderRequest(string Title, string Message, DateTime ScheduledForUtc, bool RepeatDaily, string? ClientRequestId = null);

/// <summary>Replaces a reminder's fields in place, keeping its id (PUT /api/reminders/{id}).</summary>
public sealed record UpdateReminderRequest(string Title, string Message, DateTime ScheduledForUtc, bool RepeatDaily, bool IsEnabled);

public sealed record ToggleReminderRequest(bool IsEnabled);

public sealed record ReminderResponse(
    Guid Id,
    string Title,
    string Message,
    DateTime ScheduledForUtc,
    bool RepeatDaily,
    bool IsEnabled,
    DateTime? LastTriggeredAtUtc);
