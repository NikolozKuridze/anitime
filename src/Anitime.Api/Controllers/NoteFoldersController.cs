using Anitime.Api.Infrastructure;
using Anitime.Application.Abstractions;
using Anitime.Application.Common;
using Anitime.Contracts;
using Anitime.Domain.Entities;
using Anitime.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Anitime.Api.Controllers;

/// <summary>Note folders: two levels at most (a folder with a parent can't have children).</summary>
[Authorize]
[InvalidRequestBody]
[Route("api/notes/folders")]
public sealed class NoteFoldersController : BaseApiController
{
    private const string NotFoundMessage = "Folder was not found.";

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyCollection<NoteFolderResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var userId = userContext.GetRequiredUserId();
        var folders = await db.NoteFolders.AsNoTracking()
            .Where(f => f.UserId == userId)
            .OrderBy(f => f.Name)
            .Select(f => new NoteFolderResponse(
                f.Id, f.Name, f.ParentId,
                db.Notes.Count(n => n.UserId == userId && n.FolderId == f.Id && !n.IsArchived),
                f.CreatedAtUtc, f.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
        return Ok(folders);
    }

    [HttpPost]
    [ProducesResponseType(typeof(NoteFolderResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateNoteFolderRequest request,
        [FromServices] IValidator<CreateNoteFolderRequest> validator,
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
        var parentError = await CheckParentAsync(db, userId, folderId: null, request.ParentId, cancellationToken);
        if (parentError is not null)
        {
            return FromResult(Result.Failure(parentError));
        }

        var now = clock.UtcNow;
        var folder = new NoteFolder { UserId = userId, Name = request.Name.Trim(), ParentId = request.ParentId, CreatedAtUtc = now, UpdatedAtUtc = now };
        db.NoteFolders.Add(folder);
        await db.SaveChangesAsync(cancellationToken);
        return FromCreatedResult($"/api/notes/folders/{folder.Id}",
            Result<NoteFolderResponse>.Success(new NoteFolderResponse(folder.Id, folder.Name, folder.ParentId, 0, now, now)));
    }

    /// <summary>Renames the folder and sets its parent (null makes it top-level).</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(NoteFolderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateNoteFolderRequest request,
        [FromServices] IValidator<UpdateNoteFolderRequest> validator,
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
        var folder = await db.NoteFolders.FirstOrDefaultAsync(f => f.Id == id && f.UserId == userId, cancellationToken);
        if (folder is null)
        {
            return FromResult(Result.Failure(AppErrors.NotFound(NotFoundMessage)));
        }

        var parentError = await CheckParentAsync(db, userId, id, request.ParentId, cancellationToken);
        if (parentError is not null)
        {
            return FromResult(Result.Failure(parentError));
        }

        folder.Name = request.Name.Trim();
        folder.ParentId = request.ParentId;
        folder.UpdatedAtUtc = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var count = await db.Notes.CountAsync(n => n.UserId == userId && n.FolderId == id && !n.IsArchived, cancellationToken);
        return Ok(new NoteFolderResponse(folder.Id, folder.Name, folder.ParentId, count, folder.CreatedAtUtc, folder.UpdatedAtUtc));
    }

    /// <summary>
    /// Deletes the folder; its subfolders become top-level. Its notes move out of any folder, or with
    /// <c>deleteNotes=true</c> go to Recently Deleted (as Apple Notes does).
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        Guid id,
        [FromQuery] bool deleteNotes,
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        [FromServices] IClock clock,
        CancellationToken cancellationToken)
    {
        var userId = userContext.GetRequiredUserId();
        var folder = await db.NoteFolders.FirstOrDefaultAsync(f => f.Id == id && f.UserId == userId, cancellationToken);
        if (folder is null)
        {
            return FromResult(Result.Failure(AppErrors.NotFound(NotFoundMessage)));
        }

        var now = clock.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var notes = db.Notes.Where(n => n.UserId == userId && n.FolderId == id);
        if (deleteNotes)
        {
            await notes.ExecuteUpdateAsync(s => s
                .SetProperty(n => n.IsDeleted, true)
                .SetProperty(n => n.DeletedAtUtc, now)
                .SetProperty(n => n.UpdatedAtUtc, now)
                .SetProperty(n => n.VersionToken, n => Guid.NewGuid().ToString()), cancellationToken);
        }

        // Notes in Recently Deleted keep no link to a folder that no longer exists either.
        await db.Notes.IgnoreQueryFilters().Where(n => n.UserId == userId && n.FolderId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.FolderId, (Guid?)null), cancellationToken);
        await db.NoteFolders.Where(f => f.UserId == userId && f.ParentId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.ParentId, (Guid?)null).SetProperty(f => f.UpdatedAtUtc, now), cancellationToken);
        db.NoteFolders.Remove(folder);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    private static async Task<AppError?> CheckParentAsync(AppDbContext db, Guid userId, Guid? folderId, Guid? parentId, CancellationToken cancellationToken)
    {
        if (parentId is null)
        {
            return null;
        }

        if (parentId == folderId)
        {
            return ParentError("A folder can't be its own parent.");
        }

        var parent = await db.NoteFolders.AsNoTracking()
            .Where(f => f.Id == parentId && f.UserId == userId)
            .Select(f => new { f.ParentId })
            .FirstOrDefaultAsync(cancellationToken);
        if (parent is null)
        {
            return ParentError("Parent folder was not found.");
        }

        if (parent.ParentId is not null)
        {
            return ParentError("Folders can be nested only one level deep.");
        }

        if (folderId is not null && await db.NoteFolders.AnyAsync(f => f.ParentId == folderId, cancellationToken))
        {
            return ParentError("A folder that has subfolders can't be moved into another folder.");
        }

        return null;
    }

    private static AppError ParentError(string message) =>
        AppErrors.Validation(new Dictionary<string, string[]> { ["ParentId"] = [message] });
}
