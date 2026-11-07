using Anitime.Application.Abstractions;
using Anitime.Application.Common;
using Anitime.Application.Rules;
using Anitime.Api.Infrastructure;
using Anitime.Contracts;
using Anitime.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Anitime.Api.Controllers;

[Authorize]
[Route("api/analytics")]
public sealed class AnalyticsController : BaseApiController
{
    [HttpGet]
    [ProducesResponseType(typeof(AnalyticsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        [FromServices] IClock clock,
        CancellationToken cancellationToken)
    {
        var userId = userContext.GetRequiredUserId();
        var end = to ?? ClientDateResolver.ResolveToday(Request, clock);
        var start = from ?? end.AddDays(-29);

        if (end < start)
        {
            return FromResult(Result<AnalyticsResponse>.Failure(
                AppErrors.InvalidRequest("`to` must be greater than or equal to `from`.")));
        }

        var habits = await db.Habits
            .AsNoTracking()
            .Include(x => x.Entries)
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);

        var tasks = await db.Tasks
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.Date >= start && x.Date <= end)
            .ToListAsync(cancellationToken);

        var daily = new List<DailyStatResponse>();
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            daily.Add(new DailyStatResponse(
                day,
                habits.Sum(h => h.Entries.Count(e => e.Date == day && e.IsCompleted)),
                tasks.Count(t => t.IsCompleted && t.CompletedAtUtc.HasValue && DateOnly.FromDateTime(t.CompletedAtUtc.Value) == day),
                tasks.Count(t => t.Date == day)));
        }

        var expectedHabitCompletions = habits.Sum(h =>
        {
            var habitEnd = h.StartDate.AddDays(h.DurationDays - 1);
            var effectiveStart = h.StartDate > start ? h.StartDate : start;
            var effectiveEnd = habitEnd < end ? habitEnd : end;
            return effectiveEnd < effectiveStart ? 0 : effectiveEnd.DayNumber - effectiveStart.DayNumber + 1;
        });

        var actualHabitCompletions = habits.Sum(h => h.Entries.Count(e => e.Date >= start && e.Date <= end && e.IsCompleted));
        var completedTasks = tasks.Count(t => t.IsCompleted);
        var streaks = habits.Select(x => HabitProgressCalculator.CalculateStreaks(x, end)).ToList();

        return Ok(new AnalyticsResponse(
            start,
            end,
            expectedHabitCompletions == 0 ? 0 : Math.Round(actualHabitCompletions * 100d / expectedHabitCompletions, 2),
            tasks.Count == 0 ? 0 : Math.Round(completedTasks * 100d / tasks.Count, 2),
            streaks.DefaultIfEmpty((0, 0)).Max(x => x.Item2),
            daily));
    }
}
