using Anitime.Application.Abstractions;
using Anitime.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Anitime.Infrastructure.Notes;

/// <summary>Housekeeping run by the worker: emptying Recently Deleted and removing unused note images.</summary>
public static class NotesMaintenance
{
    public static readonly TimeSpan DeletedNoteRetention = TimeSpan.FromDays(30);
    public static readonly TimeSpan OrphanAttachmentGrace = TimeSpan.FromDays(7);
    private const int AttachmentBatch = 500;

    /// <summary>Deletes forever the notes that have been in Recently Deleted for 30 days (their backups cascade).</summary>
    public static Task<int> PurgeDeletedNotesAsync(AppDbContext db, DateTime now, CancellationToken cancellationToken)
    {
        var cutoff = now - DeletedNoteRetention;
        return db.Notes.IgnoreQueryFilters()
            .Where(note => note.IsDeleted && note.DeletedAtUtc <= cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// Marks images no note references (Recently Deleted counts as a reference, so restoring keeps them), clears the mark
    /// when a note uses one again, and deletes those unreferenced for 7 days. Returns how many were deleted.
    /// </summary>
    public static async Task<int> CleanUpOrphanAttachmentsAsync(
        AppDbContext db, IFileStorageService storage, DateTime now, CancellationToken cancellationToken)
    {
        var attachments = db.NoteAttachments.IgnoreQueryFilters();
        var notes = db.Notes.IgnoreQueryFilters();

        await attachments
            .Where(a => a.UnreferencedSinceUtc != null && notes.Any(n => n.AssetIds.Contains(a.Id)))
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.UnreferencedSinceUtc, (DateTime?)null), cancellationToken);
        await attachments
            .Where(a => a.UnreferencedSinceUtc == null && !notes.Any(n => n.AssetIds.Contains(a.Id)))
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.UnreferencedSinceUtc, now), cancellationToken);

        var cutoff = now - OrphanAttachmentGrace;
        var candidates = await attachments
            .Where(a => a.UnreferencedSinceUtc <= cutoff)
            .OrderBy(a => a.UnreferencedSinceUtc)
            .Take(AttachmentBatch)
            .Select(a => new { a.Id, a.ObjectName })
            .ToListAsync(cancellationToken);
        if (candidates.Count == 0)
        {
            return 0;
        }

        // Re-checked in the delete itself, so an image a note started using a moment ago survives.
        var ids = candidates.Select(c => c.Id).ToList();
        await attachments
            .Where(a => ids.Contains(a.Id) && !notes.Any(n => n.AssetIds.Contains(a.Id)))
            .ExecuteDeleteAsync(cancellationToken);
        var kept = await attachments.Where(a => ids.Contains(a.Id)).Select(a => a.Id).ToListAsync(cancellationToken);

        var deleted = candidates.Where(c => !kept.Contains(c.Id)).ToList();
        foreach (var attachment in deleted)
        {
            await storage.DeleteAsync(attachment.ObjectName, cancellationToken);
        }

        return deleted.Count;
    }
}
