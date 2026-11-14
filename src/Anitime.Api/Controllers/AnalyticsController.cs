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
    private const int DefaultRangeDays = 30;
    private const int MaxRangeDays = 366; // the response has one row per day, computed in memory

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
        var start = from ?? DateOnly.FromDayNumber(Math.Max(0, end.DayNumber - (DefaultRangeDays - 1)));

        if (end < start)
        {
            return FromResult(Result<AnalyticsResponse>.Failure(
                AppErrors.InvalidRequest("`to` must be greater than or equal to `from`.")));
        }

        if (end.DayNumber - start.DayNumber + 1 > MaxRangeDays)
        {
            return FromResult(Result<AnalyticsResponse>.Failure(
                AppErrors.InvalidRequest($"The date range cannot exceed {MaxRangeDays} days.")));
        }

        var habits = await HabitHistoryQuery.LoadAsync(db, userId, end, cancellationToken);
        var tasks = await db.Tasks
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.Date >= start && x.Date <= end)
            .Select(x => new { x.Date, x.IsCompleted, x.CompletedAtUtc })
            .ToListAsync(cancellationToken);

        var habitCompletionsByDay = habits
            .SelectMany(h => h.CompletedDays)
            .Where(d => d >= start)
            .GroupBy(d => d)
            .ToDictionary(g => g.Key, g => g.Count());
        var tasksCompletedByDay = tasks
            .Where(t => t.IsCompleted && t.CompletedAtUtc.HasValue)
            .GroupBy(t => DateOnly.FromDateTime(t.CompletedAtUtc!.Value))
            .ToDictionary(g => g.Key, g => g.Count());
        var tasksPlannedByDay = tasks.GroupBy(t => t.Date).ToDictionary(g => g.Key, g => g.Count());

        var daily = new List<DailyStatResponse>(end.DayNumber - start.DayNumber + 1);
        for (var dayNumber = start.DayNumber; dayNumber <= end.DayNumber; dayNumber++)
        {
            var day = DateOnly.FromDayNumber(dayNumber);
            daily.Add(new DailyStatResponse(
                day,
                habitCompletionsByDay.GetValueOrDefault(day),
                tasksCompletedByDay.GetValueOrDefault(day),
                tasksPlannedByDay.GetValueOrDefault(day)));
        }

        var expectedHabitCompletions = habits.Sum(h =>
        {
            var effectiveStart = h.StartDate > start ? h.StartDate : start;
            var effectiveEnd = h.EndDate < end ? h.EndDate : end;
            return effectiveEnd < effectiveStart ? 0 : effectiveEnd.DayNumber - effectiveStart.DayNumber + 1;
        });

        var actualHabitCompletions = habitCompletionsByDay.Values.Sum();
        var completedTasks = tasks.Count(t => t.IsCompleted);
        var streaks = habits.Select(x => HabitProgressCalculator.CalculateStreaks(x.StartDate, x.DurationDays, x.CompletedDays, end)).ToList();

        return Ok(new AnalyticsResponse(
            start,
            end,
            expectedHabitCompletions == 0 ? 0 : Math.Round(actualHabitCompletions * 100d / expectedHabitCompletions, 2),
            tasks.Count == 0 ? 0 : Math.Round(completedTasks * 100d / tasks.Count, 2),
            streaks.DefaultIfEmpty((0, 0)).Max(x => x.Item2),
            daily));
    }
}
