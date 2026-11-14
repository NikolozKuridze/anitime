using System.Text;
using System.Text.Json;
using Anitime.Application.Common;
using Anitime.Application.Rules;
using Anitime.Contracts;
using Anitime.Domain.Entities;
using Anitime.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Anitime.Api.Infrastructure;

/// <summary>Everything a content write does to a note: format guard, backup, preview, and the facts derived from content.</summary>
public static class NoteContentWriter
{
    public const int MaxTitleLength = 160;
    public const int MaxPlainTextPreviewLength = 50_000;
    private const string ContentTooLargeMessage = "Note content is too large. Maximum size is 2 MB.";

    // Notes migrated from the pre-rich-text schema were stored with camelCase keys; new ones use PascalCase.
    private static readonly JsonSerializerOptions ContentReadOptions = new() { PropertyNameCaseInsensitive = true };

    public sealed record PreparedContent(string Json, string Preview, NoteContentFacts Facts, AppError? Error = null);

    /// <summary>Serializes the document, picks its preview (server-built for v2) and derives its facts.</summary>
    public static PreparedContent Prepare(RichNoteDocument document, string? clientPreview)
    {
        var json = JsonSerializer.Serialize(document);
        var facts = NoteContentIndex.Analyze(document);
        if (document.Version != RichNoteV2Schema.Version)
        {
            return new PreparedContent(json, NormalizePreview(clientPreview), facts);
        }

        if (Encoding.UTF8.GetByteCount(json) > RichNoteV2Schema.MaxContentBytes)
        {
            return new PreparedContent(json, string.Empty, facts, AppErrors.PayloadTooLarge(ContentTooLargeMessage));
        }

        return new PreparedContent(json, NormalizePreview(RichNoteV2Schema.BuildPlainText(document.Content!)), facts);
    }

    /// <summary>Every image in the content must be one of this user's uploads; one query, only when there are images.</summary>
    public static async Task<AppError?> CheckAssetsAsync(AppDbContext db, Guid userId, NoteContentFacts facts, CancellationToken cancellationToken)
    {
        if (facts.AssetIds.Count == 0)
        {
            return null;
        }

        var ids = facts.AssetIds.ToList();
        var owned = await db.NoteAttachments.CountAsync(a => a.UserId == userId && ids.Contains(a.Id), cancellationToken);
        return owned == ids.Count
            ? null
            : AppErrors.Validation(new Dictionary<string, string[]>
            {
                ["ContentJson"] = ["An image in this note isn't one of your uploads. Upload it with POST /api/notes/attachments first."]
            });
    }

    /// <summary>
    /// Writes new content over a loaded note. An older document version never replaces a newer one (an old app build
    /// can't read v2 and would save it blank), and the first v2 write keeps the v1 content in a backup row, in the same save.
    /// </summary>
    public static AppError? Replace(AppDbContext db, Note note, RichNoteDocument document, PreparedContent content, DateTime now)
    {
        var storedVersion = StoredVersion(note.ContentJson);
        if (document.Version < storedVersion)
        {
            return AppErrors.NoteFormatTooNew();
        }

        if (content.Error is not null)
        {
            return content.Error;
        }

        if (storedVersion < RichNoteV2Schema.Version && document.Version == RichNoteV2Schema.Version)
        {
            db.NoteContentBackups.Add(new NoteContentBackup
            {
                NoteId = note.Id,
                UserId = note.UserId,
                Version = storedVersion,
                ContentJson = note.ContentJson,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        note.ContentJson = content.Json;
        note.PlainTextPreview = content.Preview;
        ApplyFacts(note, content.Facts);
        return null;
    }

    public static void ApplyFacts(Note note, NoteContentFacts facts)
    {
        note.Tags = NoteContentIndex.MergeTags(facts.Tags, note.ExplicitTags);
        note.LinkedNoteIds = facts.LinkedNoteIds.ToList();
        note.AssetIds = facts.AssetIds.ToList();
        note.HasChecklist = facts.HasChecklist;
        note.HasImage = facts.HasImage;
    }

    /// <summary>Re-merges tags after the explicit ones changed without new content.</summary>
    public static void RefreshTags(Note note) =>
        note.Tags = NoteContentIndex.MergeTags(NoteContentIndex.Analyze(Deserialize(note.ContentJson)).Tags, note.ExplicitTags);

    public static List<string> NormalizeTags(IEnumerable<string> tags) =>
        tags.Select(NoteContentIndex.NormalizeTag).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

    public static RichNoteDocument Deserialize(string json) =>
        JsonSerializer.Deserialize<RichNoteDocument>(json, ContentReadOptions) ?? new RichNoteDocument(1, []);

    public static bool HasStoredContent(Note note)
    {
        var stored = Deserialize(note.ContentJson);
        return !string.IsNullOrWhiteSpace(note.PlainTextPreview) || (stored.Blocks?.Count ?? 0) > 0 || (stored.Content?.Count ?? 0) > 0;
    }

    // The preview is derived from the content, so a long note is saved with a shortened preview rather than rejected.
    public static string NormalizePreview(string? value) => CutToLength(value?.Trim() ?? string.Empty, MaxPlainTextPreviewLength);

    public static string CutToLength(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        // Never cut through an emoji: half a surrogate pair is not valid UTF-8.
        return value[..(char.IsHighSurrogate(value[maxLength - 1]) ? maxLength - 1 : maxLength)];
    }

    public static string NewVersionToken() => Guid.NewGuid().ToString("N");

    private static int StoredVersion(string json)
    {
        using var stored = JsonDocument.Parse(json);
        foreach (var property in stored.RootElement.EnumerateObject())
        {
            if (string.Equals(property.Name, "version", StringComparison.OrdinalIgnoreCase) && property.Value.TryGetInt32(out var version))
            {
                return version;
            }
        }

        return 1;
    }
}
