namespace Anitime.Domain.Entities;

/// <summary>A saved notes filter, evaluated by the server (<c>GET /api/notes?smartFolderId=</c>).</summary>
public sealed class SmartFolder : BaseEntity
{
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>The serialized <c>SmartFolderFilter</c> contract.</summary>
    public string FilterJson { get; set; } = "{}";
}
