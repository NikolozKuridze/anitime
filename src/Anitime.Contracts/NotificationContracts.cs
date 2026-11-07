namespace Anitime.Contracts;

public sealed record ReminderTriggeredIntegrationEvent(
    Guid ReminderId,
    Guid UserId,
    string Title,
    string Message,
    DateTime TriggeredAtUtc,
    DateTime ScheduledForUtc,
    bool RepeatDaily);
