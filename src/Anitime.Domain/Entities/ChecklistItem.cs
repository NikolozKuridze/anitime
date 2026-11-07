namespace Anitime.Domain.Entities;

public sealed class ChecklistItem : BaseEntity
{ 
    public Guid TaskItemId { get; set; }
    public string Title { get; set; } = default!;
    public bool IsDone { get; set; } 

    public TaskItem TaskItem { get; set; } = default!;
}
