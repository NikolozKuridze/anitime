namespace Anitime.Domain.Entities;

public sealed class Note : BaseEntity
{
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ContentJson { get; set; } = "{\"version\":1,\"blocks\":[]}";
    public string PlainTextPreview { get; set; } = string.Empty;
    public string FontFamily { get; set; } = "system-ui";
    public int FontSize { get; set; } = 16;
    public int FontWeight { get; set; } = 400;
    public string TextColor { get; set; } = "#111827";
    public string BackgroundColor { get; set; } = "#FFFFFF";
    public bool IsPinned { get; set; }
    public bool IsArchived { get; set; }
    public string VersionToken { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime? LastEditedAtUtc { get; set; }
    public User User { get; set; } = default!;
}
