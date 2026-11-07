namespace Anitime.Domain.Entities;

public sealed class Reminder : BaseEntity
{ 
    public Guid UserId { get; set; }
    public string Title { get; set; } = default!;
    public string Message { get; set; } = default!;
    public DateTime ScheduledForUtc { get; set; }
    public bool RepeatDaily { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime? LastTriggeredAtUtc { get; set; } 

    public User User { get; set; } = default!;
}
