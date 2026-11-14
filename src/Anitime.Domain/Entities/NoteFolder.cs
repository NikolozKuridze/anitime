namespace Anitime.Domain.Entities;

/// <summary>A user's notes folder. At most two levels: a folder with a parent can't have children.</summary>
public sealed class NoteFolder : BaseEntity
{
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
}
