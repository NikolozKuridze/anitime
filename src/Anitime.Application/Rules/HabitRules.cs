using Anitime.Domain.Entities;

namespace Anitime.Application.Rules;

public static class HabitRules
{
    public static DateOnly GetEndDate(Habit habit) => habit.StartDate.AddDays(habit.DurationDays - 1);

    public static bool IsWithinChallengeWindow(Habit habit, DateOnly date) =>
        date >= habit.StartDate && date <= GetEndDate(habit);

    public static string? ValidateCompletionDate(Habit habit, DateOnly date, DateOnly today)
    {
        if (date < habit.StartDate)
        {
            return "Habit cannot be completed before its start date.";
        }

        if (date > GetEndDate(habit))
        {
            return "Habit cannot be completed after the challenge end date.";
        }

        if (date > today)
        {
            return "Habit cannot be completed in the future.";
        }

        return null;
    }
}
