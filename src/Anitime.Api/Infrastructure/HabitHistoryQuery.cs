using Anitime.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Anitime.Api.Infrastructure;

public sealed record HabitHistory(DateOnly StartDate, int DurationDays, List<DateOnly> CompletedDays)
{
    public DateOnly EndDate => StartDate.AddDays(DurationDays - 1);
}

/// <summary>Loads each habit's window and completed dates only: no entity tracking and no unused columns.</summary>
public static class HabitHistoryQuery
{
    public static Task<List<HabitHistory>> LoadAsync(AppDbContext db, Guid userId, DateOnly upTo, CancellationToken cancellationToken) =>
        db.Habits
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new HabitHistory(
                x.StartDate,
                x.DurationDays,
                x.Entries.Where(e => e.IsCompleted && e.Date <= upTo).Select(e => e.Date).ToList()))
            .ToListAsync(cancellationToken);
}
