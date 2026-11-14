using Anitime.Api.Mapping;
using Anitime.Api.Infrastructure;
using Anitime.Application.Abstractions;
using Anitime.Application.Caching;
using Anitime.Application.Common;
using Anitime.Application.Rules;
using Anitime.Contracts;
using Anitime.Domain.Entities;
using Anitime.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Npgsql;

namespace Anitime.Api.Controllers;

[Authorize]
[Route("api/habits")]
public sealed class HabitsController : BaseApiController
{
    [HttpGet]
    [ConditionalGet]
    [ProducesResponseType(typeof(IReadOnlyCollection<HabitResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        [FromServices] IClock clock,
        CancellationToken cancellationToken)
    {
        var userId = userContext.GetRequiredUserId();
        var today = ClientDateResolver.ResolveToday(Request, clock);
        // Projection: only the columns the response uses (entries are the bulk of this payload).
        var items = await db.Habits
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new Habit
            {
                Id = x.Id,
                Title = x.Title,
                Emoji = x.Emoji,
                StartDate = x.StartDate,
                DurationDays = x.DurationDays,
                Entries = x.Entries
                    .OrderBy(e => e.Date)
                    .Select(e => new HabitEntry { Id = e.Id, Date = e.Date, IsCompleted = e.IsCompleted })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        return Ok(items.Select(x => x.ToResponse(today)).ToList());
    }

    [HttpPost]
    [ProducesResponseType(typeof(HabitResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateHabitRequest request,
        [FromServices] IValidator<CreateHabitRequest> validator,
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
        var today = ClientDateResolver.ResolveToday(Request, clock);
        var entity = new Habit
        {
            UserId = userContext.GetRequiredUserId(),
            Title = request.Title.Trim(),
            Emoji = string.IsNullOrWhiteSpace(request.Emoji) ? "🔥" : request.Emoji.Trim(),
            StartDate = request.StartDate ?? today,
            DurationDays = request.DurationDays,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.Habits.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync(CacheKeys.Dashboard(entity.UserId), cancellationToken);

        return FromCreatedResult($"/api/habits/{entity.Id}", Result<HabitResponse>.Success(entity.ToResponse(today)));
    }

    [HttpPost("{id:guid}/check")]
    [ProducesResponseType(typeof(HabitEntryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Toggle(
        Guid id,
        [FromBody] ToggleHabitEntryRequest request,
        [FromServices] IValidator<ToggleHabitEntryRequest> validator,
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

        var userId = userContext.GetRequiredUserId();
        var today = ClientDateResolver.ResolveToday(Request, clock);
        var targetDate = request.Date ?? today;
        // Only the target day's entry is needed, not the habit's whole history.
        var habit = await db.Habits
            .Include(x => x.Entries.Where(e => e.Date == targetDate))
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, cancellationToken);

        if (habit is null)
        {
            return FromResult(Result<HabitEntryResponse>.Failure(AppErrors.NotFound("Habit was not found.")));
        }

        var ruleError = HabitRules.ValidateCompletionDate(habit, targetDate, today);
        if (ruleError is not null)
        {
            return FromResult(Result<HabitEntryResponse>.Failure(AppErrors.BusinessRule(ruleError)));
        }

        var now = clock.UtcNow;
        var entry = habit.Entries.FirstOrDefault(x => x.Date == targetDate);
        if (entry is null)
        {
            entry = new HabitEntry
            {
                HabitId = habit.Id,
                Date = targetDate,
                IsCompleted = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            db.HabitEntries.Add(entry);
        }
        else
        {
            entry.IsCompleted = !entry.IsCompleted;
            entry.UpdatedAtUtc = now;
        }

        habit.UpdatedAtUtc = now;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_habit_entries_HabitId_Date" })
        {
            // A parallel request (double tap) created this day's entry first: return it instead of toggling again.
            var existing = await db.HabitEntries
                .AsNoTracking()
                .FirstAsync(x => x.HabitId == habit.Id && x.Date == targetDate, cancellationToken);
            return FromResult(Result<HabitEntryResponse>.Success(new HabitEntryResponse(existing.Id, existing.Date, existing.IsCompleted)));
        }

        await cache.RemoveAsync(CacheKeys.Dashboard(userId), cancellationToken);

        return FromResult(Result<HabitEntryResponse>.Success(new HabitEntryResponse(entry.Id, entry.Date, entry.IsCompleted)));
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
        var entity = await db.Habits.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, cancellationToken);
        if (entity is null)
        {
            return FromResult(Result.Failure(AppErrors.NotFound("Habit was not found.")));
        }

        db.Habits.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync(CacheKeys.Dashboard(userId), cancellationToken);
        return NoContent();
    }
}
