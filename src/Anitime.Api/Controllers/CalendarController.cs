using Anitime.Application.Abstractions;
using Anitime.Application.Common;
using Anitime.Contracts;
using Anitime.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Anitime.Api.Controllers;

[Authorize]
[Route("api/calendar")]
public sealed class CalendarController : BaseApiController
{
    private const int MinYear = 1;
    private const int MaxYear = 9998; // the month end is computed from the next month, which must exist

    [HttpGet]
    [ProducesResponseType(typeof(CalendarMonthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get(
        [FromQuery] int year,
        [FromQuery] int month,
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (month is < 1 or > 12)
        {
            return FromResult(Result<CalendarMonthResponse>.Failure(
                AppErrors.InvalidRequest("Month must be between 1 and 12.")));
        }

        if (year is < MinYear or > MaxYear)
        {
            return FromResult(Result<CalendarMonthResponse>.Failure(
                AppErrors.InvalidRequest($"Year must be between {MinYear} and {MaxYear}.")));
        }

        var userId = userContext.GetRequiredUserId();
        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1).AddDays(-1);

        var habits = await db.HabitEntries
            .AsNoTracking()
            .Where(x => x.Habit.UserId == userId && x.Date >= start && x.Date <= end && x.IsCompleted)
            .GroupBy(x => x.Date)
            .Select(x => new { Date = x.Key, Count = x.Count() })
            .ToListAsync(cancellationToken);

        var tasks = await db.Tasks
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.Date >= start && x.Date <= end)
            .ToListAsync(cancellationToken);

        var days = new List<CalendarDayResponse>();
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            days.Add(new CalendarDayResponse(
                day,
                habits.FirstOrDefault(x => x.Date == day)?.Count ?? 0,
                tasks.Count(x => x.Date == day),
                tasks.Count(x => x.Date == day && x.IsCompleted)));
        }

        return Ok(new CalendarMonthResponse(year, month, days));
    }
}
