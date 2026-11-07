namespace Anitime.Domain.Entities;

public sealed class TaskItem : BaseEntity
{ 
    public Guid UserId { get; set; }
    public string Title { get; set; } = default!;
    public string? Description { get; set; }
    public string Emoji { get; set; } = "📝";
    public string? ClientRequestId { get; set; }
    public DateOnly Date { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime? CompletedAtUtc { get; set; } 
    public User User { get; set; } = default!;
    public List<ChecklistItem> ChecklistItems { get; set; } = [];
}
