using Anitime.Application.Abstractions;
using Anitime.Application.Common;
using Anitime.Application.Validation;
using Anitime.Contracts;
using Anitime.Domain.Entities;
using Anitime.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Anitime.Api.Controllers;

[Authorize]
[Route("api/notes")]
public sealed class NotesController : BaseApiController
{
    private const string VersionConflictMessage = "Note was changed by another request. Refresh and retry.";

    // Notes migrated from the pre-rich-text schema were stored with camelCase keys; new ones use PascalCase.
    private static readonly JsonSerializerOptions ContentReadOptions = new() { PropertyNameCaseInsensitive = true };

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyCollection<NoteResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] bool includeArchived,
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

        var items = await query
            .OrderByDescending(x => x.IsPinned)
            .ThenByDescending(x => x.UpdatedAtUtc)
            .ToListAsync(cancellationToken);

        return Ok(items.Select(Map).ToList());
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

        return FromResult(Result<NoteResponse>.Success(Map(note)));
    }

    [HttpPost]
    [ProducesResponseType(typeof(NoteResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateNoteRequest request,
        [FromServices] IValidator<CreateNoteRequest> validator,
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

        var now = clock.UtcNow;
        var note = new Note
        {
            UserId = userContext.GetRequiredUserId(),
            Title = NormalizeTitle(request.Title),
            ContentJson = SerializeContent(request.ContentJson),
            PlainTextPreview = NormalizePlainTextPreview(request.PlainTextPreview),
            FontFamily = NormalizeFontFamily(request.FontFamily),
            FontSize = request.FontSize,
            FontWeight = request.FontWeight,
            TextColor = NormalizeTextColor(request.TextColor),
            BackgroundColor = NormalizeBackgroundColor(request.BackgroundColor),
            IsPinned = request.IsPinned,
            IsArchived = request.IsArchived,
            VersionToken = NewVersionToken(),
            LastEditedAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.Notes.Add(note);
        await db.SaveChangesAsync(cancellationToken);

        return FromCreatedResult($"/api/notes/{note.Id}", Result<NoteResponse>.Success(Map(note)));
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(NoteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
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

        if (request.ContentJson is null && string.IsNullOrWhiteSpace(request.Title) && !HasStoredContent(note))
        {
            return FromResult(Result<NoteResponse>.Failure(AppErrors.Validation(new Dictionary<string, string[]>
            {
                [nameof(request.Title)] = [NoteValidationRules.EmptyNoteMessage]
            })));
        }

        var now = clock.UtcNow;
        note.Title = NormalizeTitle(request.Title);
        if (request.ContentJson is not null)
        {
            note.ContentJson = SerializeContent(request.ContentJson);
            note.PlainTextPreview = NormalizePlainTextPreview(request.PlainTextPreview);
        }

        note.FontFamily = NormalizeFontFamily(request.FontFamily);
        note.FontSize = request.FontSize;
        note.FontWeight = request.FontWeight;
        note.TextColor = NormalizeTextColor(request.TextColor);
        note.BackgroundColor = NormalizeBackgroundColor(request.BackgroundColor);
        note.IsPinned = request.IsPinned;
        note.IsArchived = request.IsArchived;
        note.VersionToken = NewVersionToken();
        note.LastEditedAtUtc = now;
        note.UpdatedAtUtc = now;

        return await TrySaveAsync(db, cancellationToken)
            ? FromResult(Result<NoteResponse>.Success(Map(note)))
            : FromResult(Result<NoteResponse>.Failure(AppErrors.Conflict(VersionConflictMessage)));
    }

    [HttpPatch("{id:guid}/content")]
    [ProducesResponseType(typeof(NoteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
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
        note.ContentJson = SerializeContent(request.ContentJson);
        note.PlainTextPreview = NormalizePlainTextPreview(request.PlainTextPreview);
        note.VersionToken = NewVersionToken();
        note.LastEditedAtUtc = now;
        note.UpdatedAtUtc = now;

        return await TrySaveAsync(db, cancellationToken)
            ? FromResult(Result<NoteResponse>.Success(Map(note)))
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
        var copy = new Note
        {
            UserId = source.UserId,
            Title = string.IsNullOrWhiteSpace(source.Title) ? "Copy" : $"{source.Title} (Copy)",
            ContentJson = source.ContentJson,
            PlainTextPreview = source.PlainTextPreview,
            FontFamily = source.FontFamily,
            FontSize = source.FontSize,
            FontWeight = source.FontWeight,
            TextColor = source.TextColor,
            BackgroundColor = source.BackgroundColor,
            IsPinned = false,
            IsArchived = false,
            VersionToken = NewVersionToken(),
            LastEditedAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.Notes.Add(copy);
        await db.SaveChangesAsync(cancellationToken);
        return FromCreatedResult($"/api/notes/{copy.Id}", Result<NoteResponse>.Success(Map(copy)));
    }

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
        note.VersionToken = NewVersionToken();
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

    private static bool HasStoredContent(Note note) =>
        !string.IsNullOrWhiteSpace(note.PlainTextPreview) ||
        (DeserializeContent(note.ContentJson).Blocks?.Count ?? 0) > 0;

    private static NoteResponse Map(Note note) =>
        new(
            note.Id,
            note.Title,
            DeserializeContent(note.ContentJson),
            note.PlainTextPreview,
            note.FontFamily,
            note.FontSize,
            note.FontWeight,
            note.TextColor,
            note.BackgroundColor,
            note.IsPinned,
            note.IsArchived,
            note.VersionToken,
            note.LastEditedAtUtc,
            note.CreatedAtUtc,
            note.UpdatedAtUtc);

    private static string NormalizeTitle(string? value) => StripNul(value)?.Trim() ?? string.Empty;
    private static string NormalizePlainTextPreview(string? value) => StripNul(value)?.Trim() ?? string.Empty;
    private static string NormalizeFontFamily(string? value) => string.IsNullOrWhiteSpace(StripNul(value)) ? "system-ui" : StripNul(value)!.Trim();
    private static string NormalizeTextColor(string? value) => string.IsNullOrWhiteSpace(value) ? "#111827" : value.Trim();
    private static string NormalizeBackgroundColor(string? value) => string.IsNullOrWhiteSpace(value) ? "#FFFFFF" : value.Trim();
    private static string SerializeContent(RichNoteDocument document) =>
        JsonSerializer.Serialize(document with { Blocks = document.Blocks.Select(StripNul).ToList() });
    private static RichNoteDocument DeserializeContent(string json) => JsonSerializer.Deserialize<RichNoteDocument>(json, ContentReadOptions) ?? new RichNoteDocument(1, []);

    // PostgreSQL text and jsonb cannot store U+0000. Stripping it means a stray pasted character can't block every save.
    private static string? StripNul(string? value) => value?.Replace("\0", string.Empty);

    private static RichTextBlockNode StripNul(RichTextBlockNode block) =>
        new(StripNul(block.Type)!, StripNul(block.Align), block.Children.Select(StripNul).ToList());

    private static RichTextNode StripNul(RichTextNode node) =>
        new(
            node.TextNode is null ? null : new RichTextTextNode(
                StripNul(node.TextNode.Type)!,
                StripNul(node.TextNode.Text)!,
                node.TextNode.Marks?.Select(m => new RichTextMark(StripNul(m.Type)!, StripNul(m.Value))).ToList()),
            node.BlockNode is null ? null : StripNul(node.BlockNode));
    private static string NewVersionToken() => Guid.NewGuid().ToString("N");
}
