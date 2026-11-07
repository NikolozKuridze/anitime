namespace Anitime.Contracts;

public sealed record DashboardResponse(
    int TotalHabits,
    int ActiveHabits,
    int HabitsCompletedToday,
    int TotalTasks,
    int TasksCompletedToday,
    int PendingTasks,
    int BestHabitStreak,
    int CurrentHabitStreak);

public sealed record DailyStatResponse(DateOnly Date, int HabitCompletions, int TasksCompleted, int TasksPlanned);

public sealed record AnalyticsResponse(
    DateOnly From,
    DateOnly To,
    double HabitCompletionRate,
    double TaskCompletionRate,
    int BestStreak,
    IReadOnlyCollection<DailyStatResponse> Daily);
