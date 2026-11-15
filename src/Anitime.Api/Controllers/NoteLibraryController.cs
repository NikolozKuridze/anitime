using Anitime.Api.Infrastructure;
using Anitime.Api.Mapping;
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

/// <summary>Tags, moving, Recently Deleted, edit-clash copies, search and backlinks for notes.</summary>
[Authorize]
[InvalidRequestBody]
[Route("api/notes")]
public sealed class NoteLibraryController : BaseApiController
{
    private const int MaxQueryLength = 100;
    private const int MaxSearchTerms = 5;
    private const int DefaultSearchLimit = 50;
    private const int MaxSearchLimit = 100;
    private const string CopyPrefix = "Copy of ";
    private const string NotFoundMessage = "Note was not found.";

    [HttpGet("tags")]
    [ProducesResponseType(typeof(IReadOnlyCollection<NoteTagResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Tags(
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var userId = userContext.GetRequiredUserId();
        var rows = await db.Database.SqlQuery<TagCountRow>($"""
            SELECT t AS "Name", count(*)::int AS "NoteCount"
            FROM notes n CROSS JOIN LATERAL unnest(n."Tags") AS t
            WHERE n."UserId" = {userId} AND NOT n."IsDeleted"
            GROUP BY t
            """).ToListAsync(cancellationToken);
        return Ok(rows.OrderBy(r => r.Name, StringComparer.Ordinal).Select(r => new NoteTagResponse(r.Name, r.NoteCount)).ToList());
    }

    /// <summary>Files notes into a folder (or none). Doesn't change their version tokens, so open editors keep saving.</summary>
    [HttpPost("move")]
    [ProducesResponseType(typeof(MoveNotesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Move(
        [FromBody] MoveNotesRequest request,
        [FromServices] IValidator<MoveNotesRequest> validator,
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var validationResponse = await ValidateAsync(request, validator, cancellationToken);
        if (validationResponse is not null)
        {
            return validationResponse;
        }

        var userId = userContext.GetRequiredUserId();
        var folderError = await NoteQueries.CheckFolderAsync(db, userId, request.FolderId, cancellationToken);
        if (folderError is not null)
        {
            return FromResult(Result.Failure(folderError));
        }

        var ids = request.NoteIds.Distinct().ToList();
        var moved = await db.Notes
            .Where(n => n.UserId == userId && ids.Contains(n.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.FolderId, request.FolderId), cancellationToken);
        return Ok(new MoveNotesResponse(moved));
    }

    // ---- Recently Deleted ----

    [HttpGet("deleted")]
    [ProducesResponseType(typeof(IReadOnlyCollection<DeletedNoteResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Deleted(
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var userId = userContext.GetRequiredUserId();
        var notes = await DeletedNotes(db, userId)
            .OrderByDescending(n => n.DeletedAtUtc)
            .Select(n => new Note
            {
                Id = n.Id, Title = n.Title, PlainTextPreview = n.PlainTextPreview, FolderId = n.FolderId,
                IsLocked = n.IsLocked, DeletedAtUtc = n.DeletedAtUtc, UpdatedAtUtc = n.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken);
        return Ok(notes.Select(NoteMappings.ToDeletedResponse).ToList());
    }

    /// <summary>A note in Recently Deleted, in full, so the app can show it before restoring.</summary>
    [HttpGet("deleted/{id:guid}")]
    [ProducesResponseType(typeof(NoteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeletedById(
        Guid id,
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var note = await DeletedNotes(db, userContext.GetRequiredUserId()).FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        return note is null
            ? FromResult(Result.Failure(AppErrors.NotFound(NotFoundMessage)))
            : Ok(NoteMappings.ToResponse(note));
    }

    /// <summary>Brings a note back from Recently Deleted, into its folder if that still exists.</summary>
    [HttpPost("{id:guid}/restore")]
    [ProducesResponseType(typeof(NoteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Restore(
        Guid id,
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        [FromServices] IClock clock,
        CancellationToken cancellationToken)
    {
        var userId = userContext.GetRequiredUserId();
        var note = await DeletedNotes(db, userId).AsTracking().FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (note is null)
        {
            return FromResult(Result.Failure(AppErrors.NotFound(NotFoundMessage)));
        }

        if (note.FolderId is not null && !await db.NoteFolders.AnyAsync(f => f.Id == note.FolderId && f.UserId == userId, cancellationToken))
        {
            note.FolderId = null;
        }

        // A retry with this note's clientRequestId may have made a new note while it was deleted; that one keeps the id.
        if (note.ClientRequestId is not null &&
            await db.Notes.AnyAsync(n => n.UserId == userId && n.ClientRequestId == note.ClientRequestId, cancellationToken))
        {
            note.ClientRequestId = null;
        }

        note.IsDeleted = false;
        note.DeletedAtUtc = null;
        note.VersionToken = NoteContentWriter.NewVersionToken();
        note.UpdatedAtUtc = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(NoteMappings.ToResponse(note));
    }

    /// <summary>Deletes a note in Recently Deleted forever. 404 for a note that isn't in Recently Deleted.</summary>
    [HttpDelete("{id:guid}/forever")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteForever(
        Guid id,
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var deleted = await DeletedNotes(db, userContext.GetRequiredUserId()).Where(n => n.Id == id).ExecuteDeleteAsync(cancellationToken);
        return deleted == 0 ? FromResult(Result.Failure(AppErrors.NotFound(NotFoundMessage))) : NoContent();
    }

    /// <summary>
    /// After a 409, saves the version the user did NOT keep as a new note named "Copy of {title}", straight into
    /// Recently Deleted (timestamps set by the server). The original note is not touched.
    /// </summary>
    [HttpPost("{id:guid}/conflict-copy")]
    [ProducesResponseType(typeof(DeletedNoteResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> ConflictCopy(
        Guid id,
        [FromBody] ConflictCopyRequest request,
        [FromServices] IValidator<ConflictCopyRequest> validator,
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
        var source = await db.Notes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, cancellationToken);
        if (source is null)
        {
            return FromResult(Result.Failure(AppErrors.NotFound(NotFoundMessage)));
        }

        var content = NoteContentWriter.Prepare(request.ContentJson, request.PlainTextPreview);
        var error = content.Error ?? await NoteContentWriter.CheckAssetsAsync(db, userId, content.Facts, cancellationToken);
        if (error is not null)
        {
            return FromResult(Result.Failure(error));
        }

        var now = clock.UtcNow;
        var baseTitle = (request.Title ?? source.Title).Trim();
        var copy = NoteMappings.CopyOf(source, NoteContentWriter.CutToLength(CopyPrefix + baseTitle, NoteContentWriter.MaxTitleLength).TrimEnd(), now);
        copy.ContentJson = content.Json;
        copy.PlainTextPreview = content.Preview;
        NoteContentWriter.ApplyFacts(copy, content.Facts);
        copy.IsPinned = false;
        copy.IsDeleted = true;
        copy.DeletedAtUtc = now;

        db.Notes.Add(copy);
        await db.SaveChangesAsync(cancellationToken);
        return FromCreatedResult($"/api/notes/deleted/{copy.Id}", Result<DeletedNoteResponse>.Success(NoteMappings.ToDeletedResponse(copy)));
    }

    // ---- search and backlinks ----

    /// <summary>
    /// Case-insensitive substring search over title and text (checklists and tables included), for any script.
    /// Runs as one query over the user's own notes, newest first, using the (UserId, IsDeleted, UpdatedAtUtc) index.
    /// Every word must match. Locked notes match by title only and return no snippet. Recently Deleted is excluded.
    /// </summary>
    [HttpGet("search")]
    [ProducesResponseType(typeof(IReadOnlyCollection<NoteSearchResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Search(
        [FromQuery] string? q,
        [FromQuery] int? limit,
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var text = q?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MaxQueryLength)
        {
            return FromResult(Result.Failure(AppErrors.InvalidRequest($"q must be 1 to {MaxQueryLength} characters.")));
        }

        var terms = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxSearchTerms).ToList();
        var userId = userContext.GetRequiredUserId();
        var query = db.Notes.AsNoTracking().Where(n => n.UserId == userId);
        foreach (var term in terms)
        {
            // SearchText is already lowercase (a locked note's holds only its title), so a plain substring test does the
            // case-insensitive match. IndexOf becomes strpos(), a fast literal search ('%' and '_' need no escaping)
            // that is several times quicker than LIKE on UTF-8 text; Contains would become LIKE. No extension needed.
            var lowered = term.ToLowerInvariant();
            query = query.Where(n => n.SearchText.IndexOf(lowered) >= 0);
        }

        var hits = await query
            .OrderByDescending(n => n.UpdatedAtUtc)
            .Take(Math.Clamp(limit ?? DefaultSearchLimit, 1, MaxSearchLimit))
            .Select(n => new
            {
                n.Id, n.Title, Text = n.IsLocked ? string.Empty : n.PlainTextPreview,
                n.FolderId, n.IsPinned, n.IsArchived, n.IsLocked, n.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return Ok(hits.Select(hit =>
        {
            var (snippet, ranges) = hit.IsLocked ? (string.Empty, []) : NoteSearchSnippets.Snippet(hit.Text, terms);
            return new NoteSearchResult(hit.Id, hit.Title, NoteSearchSnippets.Ranges(hit.Title, terms), snippet, ranges,
                hit.FolderId, hit.IsPinned, hit.IsArchived, hit.IsLocked, hit.UpdatedAtUtc);
        }).ToList());
    }

    /// <summary>Notes whose content links to this one (<c>anitime-note:{id}</c>), newest first.</summary>
    [HttpGet("{id:guid}/backlinks")]
    [ProducesResponseType(typeof(IReadOnlyCollection<NoteLinkResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Backlinks(
        Guid id,
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var userId = userContext.GetRequiredUserId();
        if (!await db.Notes.AnyAsync(n => n.Id == id && n.UserId == userId, cancellationToken))
        {
            return FromResult(Result.Failure(AppErrors.NotFound(NotFoundMessage)));
        }

        // `ARRAY[id] <@ "LinkedNoteIds"`, served by the GIN index on LinkedNoteIds.
        var target = new List<Guid> { id };
        var links = await db.Notes.AsNoTracking()
            .Where(n => n.UserId == userId && n.Id != id && target.All(t => n.LinkedNoteIds.Contains(t)))
            .OrderByDescending(n => n.UpdatedAtUtc)
            .Select(n => new NoteLinkResponse(n.Id, n.Title, n.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
        return Ok(links);
    }

    private static IQueryable<Note> DeletedNotes(AppDbContext db, Guid userId) =>
        db.Notes.IgnoreQueryFilters().AsNoTracking().Where(n => n.UserId == userId && n.IsDeleted);

    private sealed class TagCountRow
    {
        public string Name { get; set; } = string.Empty;
        public int NoteCount { get; set; }
    }
}
