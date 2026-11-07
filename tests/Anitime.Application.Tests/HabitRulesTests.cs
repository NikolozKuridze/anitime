using Anitime.Application.Rules;
using Anitime.Domain.Entities;
using Xunit;

namespace Anitime.Application.Tests;

public sealed class HabitRulesTests
{
    [Fact]
    public void ValidateCompletionDate_Fails_WhenBeforeStartDate()
    {
        var habit = CreateHabit(new DateOnly(2026, 4, 10), 30);

        var error = HabitRules.ValidateCompletionDate(habit, new DateOnly(2026, 4, 9), new DateOnly(2026, 4, 20));

        Assert.Equal("Habit cannot be completed before its start date.", error);
    }

    [Fact]
    public void ValidateCompletionDate_Fails_WhenAfterChallengeEnd()
    {
        var habit = CreateHabit(new DateOnly(2026, 4, 1), 7);

        var error = HabitRules.ValidateCompletionDate(habit, new DateOnly(2026, 4, 10), new DateOnly(2026, 4, 10));

        Assert.Equal("Habit cannot be completed after the challenge end date.", error);
    }

    [Fact]
    public void ValidateCompletionDate_Fails_WhenInFuture()
    {
        var habit = CreateHabit(new DateOnly(2026, 4, 20), 30);

        var error = HabitRules.ValidateCompletionDate(habit, new DateOnly(2026, 4, 24), new DateOnly(2026, 4, 23));

        Assert.Equal("Habit cannot be completed in the future.", error);
    }

    [Fact]
    public void CalculateStreaks_IgnoresFutureAndOutOfWindowEntries()
    {
        var habit = CreateHabit(new DateOnly(2026, 4, 20), 5);
        habit.Entries.Add(new HabitEntry { Date = new DateOnly(2026, 4, 20), IsCompleted = true });
        habit.Entries.Add(new HabitEntry { Date = new DateOnly(2026, 4, 21), IsCompleted = true });
        habit.Entries.Add(new HabitEntry { Date = new DateOnly(2026, 4, 24), IsCompleted = true });
        habit.Entries.Add(new HabitEntry { Date = new DateOnly(2026, 4, 26), IsCompleted = true });

        var result = HabitProgressCalculator.CalculateStreaks(habit, new DateOnly(2026, 4, 21));

        Assert.Equal(2, result.Current);
        Assert.Equal(2, result.Best);
    }

    private static Habit CreateHabit(DateOnly startDate, int durationDays) =>
        new()
        {
            StartDate = startDate,
            DurationDays = durationDays,
            Title = "Read",
            Emoji = "📚"
        };
}
