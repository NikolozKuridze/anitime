using System.Text.Json;
using Anitime.Api.Infrastructure;
using Anitime.Api.Mapping;
using Anitime.Application.Abstractions;
using Anitime.Application.Common;
using Anitime.Application.Rules;
using Anitime.Application.Validation;
using Anitime.Contracts;
using Anitime.Domain.Entities;
using Anitime.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Anitime.Api.Controllers;

[Authorize]
[InvalidRequestBody]
[Route("api/notes")]
public sealed class NotesController : BaseApiController
{
    private const string CopySuffix = " (Copy)";
    private const string VersionConflictMessage = "Note was changed by another request. Refresh and retry.";
    private const string SummaryView = "summary";
    private const string NoFolder = "none";

    /// <param name="folderId">A folder id, or "none" for notes outside any folder.</param>
    /// <param name="tag">Notes carrying this tag ("#work" and "work" are the same).</param>
    /// <param name="smartFolderId">Notes matching that saved filter, evaluated by the server.</param>
    [HttpGet]
    [ConditionalGet]
    [ProducesResponseType(typeof(IReadOnlyCollection<NoteResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(
        [FromQuery] bool includeArchived,
        [FromQuery] string? view,
        [FromQuery] string? folderId,
        [FromQuery] string? tag,
        [FromQuery] Guid? smartFolderId,
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var userId = userContext.GetRequiredUserId();
        var query = db.Notes.AsNoTracking().Where(x => x.UserId == userId);

        if (!includeArchived)
        {
            query = query.Where(x => !x.IsArchived);
        }

        if (folderId is not null)
        {
            if (string.Equals(folderId, NoFolder, StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(x => x.FolderId == null);
            }
            else if (Guid.TryParse(folderId, out var folder))
            {
                query = query.Where(x => x.FolderId == folder);
            }
            else
            {
                return FromResult(Result.Failure(AppErrors.InvalidRequest("folderId must be a folder id or \"none\".")));
            }
        }

        if (tag is not null)
        {
            if (NoteContentIndex.NormalizeTag(tag) is not { } normalized)
            {
                return FromResult(Result.Failure(AppErrors.InvalidRequest("tag is not a valid tag.")));
            }

            query = NoteQueries.WithAllTags(query, [normalized]);
        }

        if (smartFolderId is not null)
        {
            var filterJson = await db.SmartFolders.AsNoTracking()
                .Where(x => x.Id == smartFolderId && x.UserId == userId)
                .Select(x => x.FilterJson)
                .FirstOrDefaultAsync(cancellationToken);
            if (filterJson is null)
            {
                return FromResult(Result.Failure(AppErrors.NotFound("Smart folder was not found.")));
            }

            query = NoteQueries.Apply(query, JsonSerializer.Deserialize<SmartFolderFilter>(filterJson)!);
        }

        query = query.OrderByDescending(x => x.IsPinned).ThenByDescending(x => x.UpdatedAtUtc);

        if (string.Equals(view, SummaryView, StringComparison.OrdinalIgnoreCase))
        {
            // Opt-in: skips reading and re-serializing every note's full content.
            var summaries = await query
                .Select(x => new NoteSummaryResponse(
                    x.Id, x.Title, x.PlainTextPreview, x.FontFamily, x.FontSize, x.FontWeight, x.TextColor, x.BackgroundColor,
                    x.IsPinned, x.IsArchived, x.VersionToken, x.LastEditedAtUtc, x.CreatedAtUtc, x.UpdatedAtUtc,
                    x.FolderId, x.IsLocked, x.PaperStyle, x.Tags, x.ExplicitTags, x.HasChecklist, x.HasImage))
                .ToListAsync(cancellationToken);
            return Ok(summaries);
        }

        var items = await query.ToListAsync(cancellationToken);
        return Ok(items.Select(NoteMappings.ToResponse).ToList());
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(NoteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(
        Guid id,
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var userId = userContext.GetRequiredUserId();
        var note = await db.Notes
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, cancellationToken);

        if (note is null)
        {
            return FromResult(Result<NoteResponse>.Failure(AppErrors.NotFound("Note was not found.")));
        }

        return FromResult(Result<NoteResponse>.Success(NoteMappings.ToResponse(note)));
    }

    [HttpPost]
    [ProducesResponseType(typeof(NoteResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> Create(
        [FromBody] CreateNoteRequest request,
        [FromServices] IValidator<CreateNoteRequest> validator,
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        [FromServices] IClock clock,
        CancellationToken cancellationToken)
    {
        var userId = userContext.GetRequiredUserId();
        var clientRequestId = ClientRequests.Normalize(request.ClientRequestId);
        if (await FindByClientRequestAsync(db, userId, clientRequestId, cancellationToken) is { } existing)
        {
            return Ok(NoteMappings.ToResponse(existing));
        }

        var validationResponse = await ValidateAsync(request, validator, cancellationToken);
        if (validationResponse is not null)
        {
            return validationResponse;
        }

        var content = NoteContentWriter.Prepare(request.ContentJson, request.PlainTextPreview);
        var error = content.Error
            ?? await NoteQueries.CheckFolderAsync(db, userId, request.FolderId, cancellationToken)
            ?? await NoteContentWriter.CheckAssetsAsync(db, userId, content.Facts, cancellationToken);
        if (error is not null)
        {
            return FromResult(Result<NoteResponse>.Failure(error));
        }

        var now = clock.UtcNow;
        var note = new Note
        {
            UserId = userId,
            Title = NormalizeTitle(request.Title),
            ContentJson = content.Json,
            PlainTextPreview = content.Preview,
            FontFamily = NormalizeFontFamily(request.FontFamily),
            FontSize = request.FontSize,
            FontWeight = request.FontWeight,
            TextColor = NormalizeTextColor(request.TextColor),
            BackgroundColor = NormalizeBackgroundColor(request.BackgroundColor),
            IsPinned = request.IsPinned,
            IsArchived = request.IsArchived,
            FolderId = request.FolderId,
            IsLocked = request.IsLocked ?? false,
            PaperStyle = request.PaperStyle ?? NotePaperStyles.Plain,
            ExplicitTags = NoteContentWriter.NormalizeTags(request.Tags ?? []),
            ClientRequestId = clientRequestId,
            VersionToken = NoteContentWriter.NewVersionToken(),
            LastEditedAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        NoteContentWriter.ApplyFacts(note, content.Facts);

        db.Notes.Add(note);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ClientRequests.IsDuplicate(ex, clientRequestId, "IX_notes_UserId_ClientRequestId"))
        {
            var original = await FindByClientRequestAsync(db, userId, clientRequestId, cancellationToken);
            if (original is not null)
            {
                return Ok(NoteMappings.ToResponse(original));
            }

            throw;
        }

        return FromCreatedResult($"/api/notes/{note.Id}", Result<NoteResponse>.Success(NoteMappings.ToResponse(note)));
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(NoteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateNoteRequest request,
        [FromServices] IValidator<UpdateNoteRequest> validator,
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
        var note = await db.Notes.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, cancellationToken);
        if (note is null)
        {
            return FromResult(Result<NoteResponse>.Failure(AppErrors.NotFound("Note was not found.")));
        }
        if (!string.Equals(note.VersionToken, request.VersionToken, StringComparison.Ordinal))
        {
            return FromResult(Result<NoteResponse>.Failure(AppErrors.Conflict(VersionConflictMessage)));
        }

        if (request.ContentJson is null && string.IsNullOrWhiteSpace(request.Title) && !NoteContentWriter.HasStoredContent(note))
        {
            return FromResult(Result<NoteResponse>.Failure(AppErrors.Validation(new Dictionary<string, string[]>
            {
                [nameof(request.Title)] = [NoteValidationRules.EmptyNoteMessage]
            })));
        }

        // Fields installed app builds don't send are null and keep their stored values.
        if (request.Tags is not null)
        {
            note.ExplicitTags = NoteContentWriter.NormalizeTags(request.Tags);
        }

        var now = clock.UtcNow;
        if (request.ContentJson is not null)
        {
            var content = NoteContentWriter.Prepare(request.ContentJson, request.PlainTextPreview);
            var error = NoteContentWriter.Replace(db, note, request.ContentJson, content, now)
                ?? await NoteContentWriter.CheckAssetsAsync(db, userId, content.Facts, cancellationToken);
            if (error is not null)
            {
                return FromResult(Result<NoteResponse>.Failure(error));
            }
        }
        else if (request.Tags is not null)
        {
            NoteContentWriter.RefreshTags(note);
        }

        note.Title = NormalizeTitle(request.Title);
        note.FontFamily = NormalizeFontFamily(request.FontFamily);
        note.FontSize = request.FontSize;
        note.FontWeight = request.FontWeight;
        note.TextColor = NormalizeTextColor(request.TextColor);
        note.BackgroundColor = NormalizeBackgroundColor(request.BackgroundColor);
        note.IsPinned = request.IsPinned;
        note.IsArchived = request.IsArchived;
        note.IsLocked = request.IsLocked ?? note.IsLocked;
        note.PaperStyle = request.PaperStyle ?? note.PaperStyle;
        note.VersionToken = NoteContentWriter.NewVersionToken();
        note.LastEditedAtUtc = now;
        note.UpdatedAtUtc = now;

        return await TrySaveAsync(db, cancellationToken)
            ? FromResult(Result<NoteResponse>.Success(NoteMappings.ToResponse(note)))
            : FromResult(Result<NoteResponse>.Failure(AppErrors.Conflict(VersionConflictMessage)));
    }

    [HttpPatch("{id:guid}/content")]
    [ProducesResponseType(typeof(NoteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> PatchContent(
        Guid id,
        [FromBody] UpdateNoteContentRequest request,
        [FromServices] IValidator<UpdateNoteContentRequest> validator,
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
        var note = await db.Notes.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, cancellationToken);
        if (note is null)
        {
            return FromResult(Result<NoteResponse>.Failure(AppErrors.NotFound("Note was not found.")));
        }
        if (!string.Equals(note.VersionToken, request.VersionToken, StringComparison.Ordinal))
        {
            return FromResult(Result<NoteResponse>.Failure(AppErrors.Conflict(VersionConflictMessage)));
        }

        var now = clock.UtcNow;
        var content = NoteContentWriter.Prepare(request.ContentJson, request.PlainTextPreview);
        var error = NoteContentWriter.Replace(db, note, request.ContentJson, content, now)
            ?? await NoteContentWriter.CheckAssetsAsync(db, userId, content.Facts, cancellationToken);
        if (error is not null)
        {
            return FromResult(Result<NoteResponse>.Failure(error));
        }

        note.VersionToken = NoteContentWriter.NewVersionToken();
        note.LastEditedAtUtc = now;
        note.UpdatedAtUtc = now;

        return await TrySaveAsync(db, cancellationToken)
            ? FromResult(Result<NoteResponse>.Success(NoteMappings.ToResponse(note)))
            : FromResult(Result<NoteResponse>.Failure(AppErrors.Conflict(VersionConflictMessage)));
    }

    [HttpPost("{id:guid}/duplicate")]
    [ProducesResponseType(typeof(NoteResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Duplicate(
        Guid id,
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        [FromServices] IClock clock,
        CancellationToken cancellationToken)
    {
        var userId = userContext.GetRequiredUserId();
        var source = await db.Notes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, cancellationToken);
        if (source is null)
        {
            return FromResult(Result<NoteResponse>.Failure(AppErrors.NotFound("Note was not found.")));
        }

        var now = clock.UtcNow;
        var copy = NoteMappings.CopyOf(source, BuildCopyTitle(source.Title), now);
        copy.IsPinned = false;
        copy.IsArchived = false;

        db.Notes.Add(copy);
        await db.SaveChangesAsync(cancellationToken);
        return FromCreatedResult($"/api/notes/{copy.Id}", Result<NoteResponse>.Success(NoteMappings.ToResponse(copy)));
    }

    /// <summary>Moves the note to Recently Deleted; it is deleted forever after 30 days.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        Guid id,
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var userId = userContext.GetRequiredUserId();
        var note = await db.Notes.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, cancellationToken);
        if (note is null)
        {
            return FromResult(Result.Failure(AppErrors.NotFound("Note was not found.")));
        }

        // Rotating the token makes a save already in flight fail with 409 instead of editing the deleted note.
        note.VersionToken = NoteContentWriter.NewVersionToken();
        db.Notes.Remove(note);
        return await TrySaveAsync(db, cancellationToken)
            ? NoContent()
            : FromResult(Result.Failure(AppErrors.Conflict(VersionConflictMessage)));
    }

    /// <summary>Returns false when another request changed the note after it was loaded (VersionToken is a concurrency token).</summary>
    private static async Task<bool> TrySaveAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    private static string NormalizeTitle(string? value) => value?.Trim() ?? string.Empty;
    private static string NormalizeFontFamily(string? value) => string.IsNullOrWhiteSpace(value) ? "system-ui" : value.Trim();
    private static string NormalizeTextColor(string? value) => string.IsNullOrWhiteSpace(value) ? "#111827" : value.Trim();
    private static string NormalizeBackgroundColor(string? value) => string.IsNullOrWhiteSpace(value) ? "#FFFFFF" : value.Trim();

    private static string BuildCopyTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "Copy";
        }

        return NoteContentWriter.CutToLength(title, NoteContentWriter.MaxTitleLength - CopySuffix.Length) + CopySuffix;
    }

    private static Task<Note?> FindByClientRequestAsync(AppDbContext db, Guid userId, string? clientRequestId, CancellationToken cancellationToken) =>
        ClientRequests.CanLookUp(clientRequestId)
            ? db.Notes
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserId == userId && x.ClientRequestId == clientRequestId, cancellationToken)
            : Task.FromResult<Note?>(null);
}
