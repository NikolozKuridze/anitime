namespace Anitime.Contracts;

public sealed record CalendarDayResponse(DateOnly Date, int HabitCompletions, int TasksDue, int TasksCompleted);

public sealed record CalendarMonthResponse(int Year, int Month, IReadOnlyCollection<CalendarDayResponse> Days);
