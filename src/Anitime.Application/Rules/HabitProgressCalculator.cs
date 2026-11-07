using Anitime.Domain.Entities;

namespace Anitime.Application.Rules;

public static class HabitProgressCalculator
{
    public static (int Current, int Best) CalculateStreaks(Habit habit, DateOnly today)
    {
        var endDate = HabitRules.GetEndDate(habit);
        var completedDates = habit.Entries
            .Where(x => x.IsCompleted)
            .Select(x => x.Date)
            .Where(x => x >= habit.StartDate && x <= endDate && x <= today)
            .OrderBy(x => x.DayNumber)
            .Distinct()
            .ToList();

        if (completedDates.Count == 0)
        {
            return (0, 0);
        }

        var best = 1;
        var currentRun = 1;
        for (var i = 1; i < completedDates.Count; i++)
        {
            if (completedDates[i].DayNumber - completedDates[i - 1].DayNumber == 1)
            {
                currentRun++;
                best = Math.Max(best, currentRun);
            }
            else
            {
                currentRun = 1;
            }
        }

        var current = completedDates[^1] == today ? 1 : 0;
        if (current == 1)
        {
            for (var i = completedDates.Count - 1; i > 0; i--)
            {
                if (completedDates[i].DayNumber - completedDates[i - 1].DayNumber == 1)
                {
                    current++;
                }
                else
                {
                    break;
                }
            }
        }

        return (current, best);
    }
}
