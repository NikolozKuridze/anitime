namespace Anitime.Domain.Entities;

/// <summary>
/// Copy of a note's content taken just before a newer document version first overwrote it,
/// so a bad migration can be rolled back per note without restoring the database.
/// </summary>
public sealed class NoteContentBackup : BaseEntity
{
    public Guid NoteId { get; set; }
    public Guid UserId { get; set; }

    /// <summary>The document version of <see cref="ContentJson"/> (1 for the pre-v2 format).</summary>
    public int Version { get; set; }

    public string ContentJson { get; set; } = string.Empty;
}
