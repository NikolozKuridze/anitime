using Anitime.Api.Infrastructure;
using Anitime.Contracts;
using Anitime.Domain.Entities;
using Anitime.Infrastructure.Notes;

namespace Anitime.Api.Mapping;

public static class NoteMappings
{
    public static NoteResponse ToResponse(Note note) =>
        new(
            note.Id,
            note.Title,
            NoteContentWriter.Deserialize(note.ContentJson),
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
            note.UpdatedAtUtc,
            note.FolderId,
            note.IsLocked,
            note.PaperStyle,
            note.Tags,
            note.ExplicitTags,
            note.HasChecklist,
            note.HasImage);

    public static DeletedNoteResponse ToDeletedResponse(Note note)
    {
        var deletedAt = note.DeletedAtUtc ?? note.UpdatedAtUtc;
        return new DeletedNoteResponse(
            note.Id, note.Title, note.PlainTextPreview, note.FolderId, note.IsLocked,
            deletedAt, deletedAt + NotesMaintenance.DeletedNoteRetention, note.UpdatedAtUtc);
    }

    /// <summary>A new note with the source's content, look and organisation, created now.</summary>
    public static Note CopyOf(Note source, string title, DateTime now) =>
        new()
        {
            UserId = source.UserId,
            Title = title,
            ContentJson = source.ContentJson,
            PlainTextPreview = source.PlainTextPreview,
            FontFamily = source.FontFamily,
            FontSize = source.FontSize,
            FontWeight = source.FontWeight,
            TextColor = source.TextColor,
            BackgroundColor = source.BackgroundColor,
            IsPinned = source.IsPinned,
            IsArchived = source.IsArchived,
            FolderId = source.FolderId,
            IsLocked = source.IsLocked,
            PaperStyle = source.PaperStyle,
            Tags = [.. source.Tags],
            ExplicitTags = [.. source.ExplicitTags],
            LinkedNoteIds = [.. source.LinkedNoteIds],
            AssetIds = [.. source.AssetIds],
            HasChecklist = source.HasChecklist,
            HasImage = source.HasImage,
            VersionToken = NoteContentWriter.NewVersionToken(),
            LastEditedAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
}
