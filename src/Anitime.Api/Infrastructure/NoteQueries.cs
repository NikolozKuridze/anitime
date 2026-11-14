using Anitime.Application.Common;
using Anitime.Application.Rules;
using Anitime.Application.Validation;
using Anitime.Contracts;
using Anitime.Domain.Entities;
using Anitime.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Anitime.Api.Infrastructure;

/// <summary>Note filters shared by the list, tag and smart-folder endpoints. Each one stays inside the same single query.</summary>
public static class NoteQueries
{
    // Written as "every wanted tag is in note.Tags" so Npgsql emits `wanted <@ "Tags"`, which the GIN index on Tags serves.
    public static IQueryable<Note> WithAllTags(IQueryable<Note> query, List<string> wanted) =>
        query.Where(x => wanted.All(tag => x.Tags.Contains(tag)));

    // Emits `wanted && "Tags"` (overlap), also served by the GIN index.
    public static IQueryable<Note> WithAnyTag(IQueryable<Note> query, List<string> wanted) =>
        query.Where(x => wanted.Any(tag => x.Tags.Contains(tag)));

    public static IQueryable<Note> Apply(IQueryable<Note> query, SmartFolderFilter filter)
    {
        var tags = (filter.Tags ?? []).Select(NoteContentIndex.NormalizeTag).OfType<string>().ToList();
        if (tags.Count > 0)
        {
            query = filter.TagMode == NoteLibraryRules.AllTags ? WithAllTags(query, tags) : WithAnyTag(query, tags);
        }

        if (filter.HasChecklist is { } hasChecklist)
        {
            query = query.Where(x => x.HasChecklist == hasChecklist);
        }

        if (filter.HasImage is { } hasImage)
        {
            query = query.Where(x => x.HasImage == hasImage);
        }

        if (filter.IsPinned is { } isPinned)
        {
            query = query.Where(x => x.IsPinned == isPinned);
        }

        if (filter.IsLocked is { } isLocked)
        {
            query = query.Where(x => x.IsLocked == isLocked);
        }

        if (filter.CreatedFrom is { } createdFrom)
        {
            var from = StartOfDay(createdFrom);
            query = query.Where(x => x.CreatedAtUtc >= from);
        }

        if (filter.CreatedTo is { } createdTo)
        {
            var to = StartOfDay(createdTo.AddDays(1));
            query = query.Where(x => x.CreatedAtUtc < to);
        }

        if (filter.EditedFrom is { } editedFrom)
        {
            var from = StartOfDay(editedFrom);
            query = query.Where(x => x.UpdatedAtUtc >= from);
        }

        if (filter.EditedTo is { } editedTo)
        {
            var to = StartOfDay(editedTo.AddDays(1));
            query = query.Where(x => x.UpdatedAtUtc < to);
        }

        return query;
    }

    /// <summary>A note may only be filed in one of the user's own, existing folders.</summary>
    public static async Task<AppError?> CheckFolderAsync(AppDbContext db, Guid userId, Guid? folderId, CancellationToken cancellationToken) =>
        folderId is null || await db.NoteFolders.AnyAsync(x => x.Id == folderId && x.UserId == userId, cancellationToken)
            ? null
            : AppErrors.Validation(new Dictionary<string, string[]> { ["FolderId"] = ["Folder was not found."] });

    private static DateTime StartOfDay(DateOnly day) => day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
}
