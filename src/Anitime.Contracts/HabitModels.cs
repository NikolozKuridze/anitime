namespace Anitime.Contracts;

public sealed record CreateHabitRequest(string Title, string Emoji, DateOnly? StartDate, int DurationDays);

public sealed record ToggleHabitEntryRequest(DateOnly? Date);

public sealed record HabitEntryResponse(Guid Id, DateOnly Date, bool IsCompleted);

public sealed record HabitResponse(
    Guid Id,
    string Title,
    string Emoji,
    DateOnly StartDate,
    int DurationDays,
    int CurrentStreak,
    int BestStreak,
    int CompletedDays,
    IReadOnlyCollection<HabitEntryResponse> Entries);
