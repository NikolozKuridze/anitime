using System.Text.Json;
using Anitime.Application.Abstractions;
using Anitime.Application.Caching;
using Anitime.Application.Rules;
using Anitime.Contracts;
using Anitime.Infrastructure.Persistence;
using Anitime.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace Anitime.Api.Controllers;

[Authorize]
[Route("api/dashboard")]
public sealed class DashboardController : BaseApiController
{
    private const char DayTagSeparator = '|';

    [HttpGet]
    [ProducesResponseType(typeof(DashboardResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        [FromServices] IDistributedCache cache,
        [FromServices] IClock clock,
        CancellationToken cancellationToken)
    {
        var userId = userContext.GetRequiredUserId();
        var today = ClientDateResolver.ResolveToday(Request, clock);
        var cacheKey = CacheKeys.Dashboard(userId);
        // The figures depend on the client's "today", so the entry records which day it was computed for.
        var dayTag = today.ToString("yyyy-MM-dd") + DayTagSeparator;
        var cached = await cache.GetStringAsync(cacheKey, cancellationToken);
        if (cached is not null && cached.StartsWith(dayTag, StringComparison.Ordinal))
        {
            return Content(cached[dayTag.Length..], "application/json");
        }

        var habits = await HabitHistoryQuery.LoadAsync(db, userId, today, cancellationToken);
        var todayStartUtc = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var tomorrowStartUtc = todayStartUtc.AddDays(1);
        // One aggregate row instead of every task the user ever made.
        var tasks = await db.Tasks
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                CompletedToday = g.Count(x => x.CompletedAtUtc >= todayStartUtc && x.CompletedAtUtc < tomorrowStartUtc),
                Pending = g.Count(x => !x.IsCompleted)
            })
            .FirstOrDefaultAsync(cancellationToken);

        var streaks = habits
            .Select(x => HabitProgressCalculator.CalculateStreaks(x.StartDate, x.DurationDays, x.CompletedDays, today))
            .ToList();
        var response = new DashboardResponse(
            habits.Count,
            habits.Count(x => x.EndDate >= today),
            habits.Count(x => x.CompletedDays.Contains(today)),
            tasks?.Total ?? 0,
            tasks?.CompletedToday ?? 0,
            tasks?.Pending ?? 0,
            streaks.DefaultIfEmpty((0, 0)).Max(x => x.Item2),
            streaks.Sum(x => x.Current));

        await cache.SetStringAsync(
            cacheKey,
            dayTag + JsonSerializer.Serialize(response, CacheJson.Options),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) },
            cancellationToken);

        return Ok(response);
    }
}
