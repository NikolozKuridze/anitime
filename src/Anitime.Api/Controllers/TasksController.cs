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
using Npgsql;

namespace Anitime.Api.Controllers;

[Authorize]
[Route("api/tasks")]
public sealed class TasksController : BaseApiController
{
    [HttpGet]
    [ConditionalGet]
    [ProducesResponseType(typeof(IReadOnlyCollection<TaskResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (from > to)
        {
            return FromResult(Result<IReadOnlyCollection<TaskResponse>>.Failure(
                AppErrors.InvalidRequest("`to` must be greater than or equal to `from`.")));
        }

        var userId = userContext.GetRequiredUserId();
        var query = db.Tasks.AsNoTracking().Where(x => x.UserId == userId);
        if (from is not null)
        {
            query = query.Where(x => x.Date >= from);
        }

        if (to is not null)
        {
            query = query.Where(x => x.Date <= to);
        }

        // Projection: only the columns the response uses.
        var items = await query
            .OrderBy(x => x.Date)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => new TaskItem
            {
                Id = x.Id,
                Title = x.Title,
                Description = x.Description,
                Emoji = x.Emoji,
                Date = x.Date,
                IsCompleted = x.IsCompleted,
                ChecklistItems = x.ChecklistItems
                    .OrderBy(i => i.CreatedAtUtc)
                    .Select(i => new ChecklistItem { Id = i.Id, Title = i.Title, IsDone = i.IsDone, CreatedAtUtc = i.CreatedAtUtc })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        return Ok(items.Select(x => x.ToResponse()).ToList());
    }

    [HttpPost]
    [ProducesResponseType(typeof(TaskResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateTaskRequest request,
        [FromServices] IValidator<CreateTaskRequest> validator,
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
        var userId = userContext.GetRequiredUserId();
        var normalizedTitle = request.Title.Trim();
        var normalizedDescription = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        var normalizedEmoji = string.IsNullOrWhiteSpace(request.Emoji) ? "📝" : request.Emoji.Trim();
        var normalizedClientRequestId = string.IsNullOrWhiteSpace(request.ClientRequestId) ? null : request.ClientRequestId.Trim();

        if (!string.IsNullOrWhiteSpace(normalizedClientRequestId))
        {
            var existingByRequestId = await db.Tasks
                .AsNoTracking()
                .Include(x => x.ChecklistItems.OrderBy(i => i.CreatedAtUtc))
                .FirstOrDefaultAsync(
                    x => x.UserId == userId && x.ClientRequestId == normalizedClientRequestId,
                    cancellationToken);

            if (existingByRequestId is not null)
            {
                return Ok(existingByRequestId.ToResponse());
            }
        }

        var normalizedChecklistTitles = (request.ChecklistItems ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x.Title))
            .Select(x => x.Title.Trim())
            .ToList();

        var task = new TaskItem
        {
            UserId = userId,
            Title = normalizedTitle,
            Description = normalizedDescription,
            Emoji = normalizedEmoji,
            ClientRequestId = normalizedClientRequestId,
            Date = request.Date,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        foreach (var title in normalizedChecklistTitles)
        {
            task.ChecklistItems.Add(new ChecklistItem
            {
                Title = title,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        db.Tasks.Add(task);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (
            !string.IsNullOrWhiteSpace(normalizedClientRequestId) &&
            ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_tasks_UserId_ClientRequestId" })
        {
            var existingTask = await db.Tasks
                .AsNoTracking()
                .Include(x => x.ChecklistItems.OrderBy(i => i.CreatedAtUtc))
                .FirstOrDefaultAsync(
                    x => x.UserId == userId && x.ClientRequestId == normalizedClientRequestId,
                    cancellationToken);

            if (existingTask is not null)
            {
                return Ok(existingTask.ToResponse());
            }

            throw;
        }

        await cache.RemoveAsync(CacheKeys.Dashboard(task.UserId), cancellationToken);

        return FromCreatedResult($"/api/tasks/{task.Id}", Result<TaskResponse>.Success(task.ToResponse()));
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(TaskResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateTaskRequest request,
        [FromServices] IValidator<UpdateTaskRequest> validator,
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
        var task = await db.Tasks
            .Include(x => x.ChecklistItems)
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, cancellationToken);

        if (task is null)
        {
            return FromResult(Result<TaskResponse>.Failure(AppErrors.NotFound("Task was not found.")));
        }

        task.Title = request.Title.Trim();
        task.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        task.Emoji = string.IsNullOrWhiteSpace(request.Emoji) ? "📝" : request.Emoji.Trim();
        task.Date = request.Date;
        task.UpdatedAtUtc = clock.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync(CacheKeys.Dashboard(userId), cancellationToken);
        return FromResult(Result<TaskResponse>.Success(task.ToResponse()));
    }

    [HttpPost("{id:guid}/toggle")]
    [ProducesResponseType(typeof(TaskResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Toggle(
        Guid id,
        [FromBody] ToggleTaskCompletionRequest request,
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        [FromServices] IDistributedCache cache,
        [FromServices] IClock clock,
        CancellationToken cancellationToken)
    {
        var userId = userContext.GetRequiredUserId();
        var task = await db.Tasks
            .Include(x => x.ChecklistItems)
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, cancellationToken);

        if (task is null)
        {
            return FromResult(Result<TaskResponse>.Failure(AppErrors.NotFound("Task was not found.")));
        }

        var isCompleted = request.IsCompleted ?? !task.IsCompleted;
        var now = clock.UtcNow;
        task.IsCompleted = isCompleted;
        task.CompletedAtUtc = isCompleted ? now : null;
        task.UpdatedAtUtc = now;

        await db.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync(CacheKeys.Dashboard(userId), cancellationToken);
        return FromResult(Result<TaskResponse>.Success(task.ToResponse()));
    }

    [HttpPost("{id:guid}/checklist")]
    [ProducesResponseType(typeof(ChecklistItemResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddChecklistItem(
        Guid id,
        [FromBody] ChecklistItemRequest request,
        [FromServices] IValidator<ChecklistItemRequest> validator,
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        [FromServices] IClock clock,
        CancellationToken cancellationToken)
    {
        var validationResponse = await ValidateAsync(request, validator, cancellationToken);
        if (validationResponse is not null)
        {
            return validationResponse;
        }

        var userId = userContext.GetRequiredUserId();
        var task = await db.Tasks
            .Include(x => x.ChecklistItems)
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, cancellationToken);

        if (task is null)
        {
            return FromResult(Result.Failure(AppErrors.NotFound("Task was not found.")));
        }

        var item = new ChecklistItem
        {
            TaskItemId = task.Id,
            Title = request.Title.Trim(),
            CreatedAtUtc = clock.UtcNow,
            UpdatedAtUtc = clock.UtcNow
        };

        db.ChecklistItems.Add(item);
        task.UpdatedAtUtc = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return FromCreatedResult(
            $"/api/tasks/{task.Id}/checklist/{item.Id}",
            Result<ChecklistItemResponse>.Success(new ChecklistItemResponse(item.Id, item.Title, item.IsDone)));
    }

    [HttpPost("checklist/{itemId:guid}/toggle")]
    [ProducesResponseType(typeof(ChecklistItemResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ToggleChecklistItem(
        Guid itemId,
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        [FromServices] IClock clock,
        CancellationToken cancellationToken)
    {
        var userId = userContext.GetRequiredUserId();
        var item = await db.ChecklistItems
            .Include(x => x.TaskItem)
            .FirstOrDefaultAsync(x => x.Id == itemId && x.TaskItem.UserId == userId, cancellationToken);

        if (item is null)
        {
            return FromResult(Result<ChecklistItemResponse>.Failure(AppErrors.NotFound("Checklist item was not found.")));
        }

        item.IsDone = !item.IsDone;
        item.UpdatedAtUtc = clock.UtcNow;
        item.TaskItem.UpdatedAtUtc = clock.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return FromResult(Result<ChecklistItemResponse>.Success(new ChecklistItemResponse(item.Id, item.Title, item.IsDone)));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(DeleteTaskResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        Guid id,
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        [FromServices] IDistributedCache cache,
        CancellationToken cancellationToken)
    {
        var userId = userContext.GetRequiredUserId();
        var task = await db.Tasks.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, cancellationToken);
        if (task is null)
        {
            return FromResult(Result.Failure(AppErrors.NotFound("Task was not found.")));
        }

        db.Tasks.Remove(task);
        await db.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync(CacheKeys.Dashboard(userId), cancellationToken);
        return Ok(new DeleteTaskResponse(id));
    }
}
