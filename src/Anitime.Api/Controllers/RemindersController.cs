using Anitime.Api.Infrastructure;
using Anitime.Api.Mapping;
using Anitime.Application.Abstractions;
using Anitime.Application.Caching;
using Anitime.Application.Common;
using Anitime.Contracts;
using Anitime.Domain.Entities;
using Anitime.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace Anitime.Api.Controllers;

[Authorize]
[Route("api/reminders")]
public sealed class RemindersController : BaseApiController
{
    [HttpGet]
    [ConditionalGet]
    [ProducesResponseType(typeof(IReadOnlyCollection<ReminderResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var userId = userContext.GetRequiredUserId();
        var items = await db.Reminders
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.ScheduledForUtc)
            .ToListAsync(cancellationToken);

        return Ok(items.Select(x => x.ToResponse()).ToList());
    }

    [HttpPost]
    [ProducesResponseType(typeof(ReminderResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateReminderRequest request,
        [FromServices] IValidator<CreateReminderRequest> validator,
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        [FromServices] IDistributedCache cache,
        [FromServices] IClock clock,
        CancellationToken cancellationToken)
    {
        var validationResponse = await ValidateAsync(request, validator, cancellationToken);
        if (validationResponse is not null)
        {
            return validationResponse;
        }

        var now = clock.UtcNow;
        var reminder = new Reminder
        {
            UserId = userContext.GetRequiredUserId(),
            Title = request.Title.Trim(),
            Message = request.Message.Trim(),
            ScheduledForUtc = request.ScheduledForUtc,
            RepeatDaily = request.RepeatDaily,
            IsEnabled = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.Reminders.Add(reminder);
        await db.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync(CacheKeys.Dashboard(reminder.UserId), cancellationToken);

        return FromCreatedResult($"/api/reminders/{reminder.Id}", Result<ReminderResponse>.Success(reminder.ToResponse()));
    }

    [HttpPost("{id:guid}/toggle")]
    [ProducesResponseType(typeof(ReminderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Toggle(
        Guid id,
        [FromBody] ToggleReminderRequest request,
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        [FromServices] IClock clock,
        CancellationToken cancellationToken)
    {
        var userId = userContext.GetRequiredUserId();
        var reminder = await db.Reminders.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, cancellationToken);
        if (reminder is null)
        {
            return FromResult(Result<ReminderResponse>.Failure(AppErrors.NotFound("Reminder was not found.")));
        }

        reminder.IsEnabled = request.IsEnabled;
        reminder.UpdatedAtUtc = clock.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return FromResult(Result<ReminderResponse>.Success(reminder.ToResponse()));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        Guid id,
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        [FromServices] IDistributedCache cache,
        CancellationToken cancellationToken)
    {
        var userId = userContext.GetRequiredUserId();
        var reminder = await db.Reminders.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, cancellationToken);
        if (reminder is null)
        {
            return FromResult(Result.Failure(AppErrors.NotFound("Reminder was not found.")));
        }

        db.Reminders.Remove(reminder);
        await db.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync(CacheKeys.Dashboard(userId), cancellationToken);
        return NoContent();
    }
}
