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
        var cached = await cache.GetStringAsync(cacheKey, cancellationToken);
        if (!string.IsNullOrWhiteSpace(cached))
        {
            return Content(cached, "application/json");
        }

        var habits = await db.Habits
            .AsNoTracking()
            .Include(x => x.Entries)
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);

        var tasks = await db.Tasks
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);

        var streaks = habits.Select(x => HabitProgressCalculator.CalculateStreaks(x, today)).ToList();
        var response = new DashboardResponse(
            habits.Count,
            habits.Count(x => x.StartDate.AddDays(x.DurationDays - 1) >= today),
            habits.Count(x => x.Entries.Any(e => e.Date == today && e.IsCompleted)),
            tasks.Count,
            tasks.Count(x => x.CompletedAtUtc.HasValue && DateOnly.FromDateTime(x.CompletedAtUtc.Value) == today),
            tasks.Count(x => !x.IsCompleted),
            streaks.DefaultIfEmpty((0, 0)).Max(x => x.Item2),
            streaks.Sum(x => x.Current));

        await cache.SetStringAsync(
            cacheKey,
            JsonSerializer.Serialize(response, CacheJson.Options),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) },
            cancellationToken);

        return Ok(response);
    }
}
