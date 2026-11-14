namespace Anitime.Domain.Entities;

/// <summary>An image uploaded for notes. Notes reference it by id from image nodes (<c>attrs.assetId</c>).</summary>
public sealed class NoteAttachment : BaseEntity
{
    public Guid UserId { get; set; }
    public string ObjectName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }

    /// <summary>Set by the cleanup job when no note (including Recently Deleted) uses it; cleared when one does again.</summary>
    public DateTime? UnreferencedSinceUtc { get; set; }
}
