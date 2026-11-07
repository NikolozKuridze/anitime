using Anitime.Domain.Enums;

namespace Anitime.Domain.Entities;

public sealed class User : BaseEntity
{ 
    public string Email { get; set; } = default!;
    public string PasswordHash { get; set; } = default!;
    public string Name { get; set; } = default!;
    public Gender Gender { get; set; } = Gender.Unspecified;
    public DateOnly? DateOfBirth { get; set; }
    public string? ProfileImageObjectName { get; set; }
    public string? ProfileImageContentType { get; set; }
    public string? BackgroundImageObjectName { get; set; }
    public string? BackgroundImageContentType { get; set; }
    public List<Habit> Habits { get; set; } = [];
    public List<TaskItem> Tasks { get; set; } = [];
    public List<Note> Notes { get; set; } = [];
    public List<Reminder> Reminders { get; set; } = [];
    public List<PasswordResetToken> PasswordResetTokens { get; set; } = [];
    public List<RefreshToken> RefreshTokens { get; set; } = [];
    public UserSettings? Settings { get; set; }
}
