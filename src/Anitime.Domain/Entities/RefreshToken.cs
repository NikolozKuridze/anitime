namespace Anitime.Domain.Entities;

public sealed class RefreshToken : BaseEntity
{
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = default!;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public DateTime? ReplacedAtUtc { get; set; }
    public string? ReplacedByTokenHash { get; set; }

    public User User { get; set; } = default!;
}
