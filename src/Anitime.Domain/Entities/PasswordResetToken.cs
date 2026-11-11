namespace Anitime.Domain.Entities;

public sealed class PasswordResetToken : BaseEntity
{ 
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = default!;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }

    /// <summary>Verification attempts made against an emailed code; a code is burned once this reaches the limit.</summary>
    public int AttemptCount { get; set; }

    public User User { get; set; } = default!;
}
