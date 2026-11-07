namespace Anitime.Domain.Entities;

public sealed class PasswordResetToken : BaseEntity
{ 
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = default!;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; } 

    public User User { get; set; } = default!;
}
