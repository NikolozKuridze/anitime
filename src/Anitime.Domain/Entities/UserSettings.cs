using Anitime.Domain.Enums;

namespace Anitime.Domain.Entities;

public sealed class UserSettings : BaseEntity
{
    public Guid UserId { get; set; }
    public BackgroundStyleType BackgroundStyleType { get; set; } = BackgroundStyleType.Transparent;
    public string? BackgroundPrimaryColor { get; set; }
    public string? BackgroundSecondaryColor { get; set; }
    public string? AccentColor { get; set; }
    public string? Locale { get; set; }
    public string? TimeZone { get; set; }

    public User User { get; set; } = default!;
}
