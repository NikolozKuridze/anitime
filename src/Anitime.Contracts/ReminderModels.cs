namespace Anitime.Contracts;

public sealed record CreateReminderRequest(string Title, string Message, DateTime ScheduledForUtc, bool RepeatDaily);

public sealed record ToggleReminderRequest(bool IsEnabled);

public sealed record ReminderResponse(
    Guid Id,
    string Title,
    string Message,
    DateTime ScheduledForUtc,
    bool RepeatDaily,
    bool IsEnabled,
    DateTime? LastTriggeredAtUtc);
