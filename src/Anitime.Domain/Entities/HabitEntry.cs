namespace Anitime.Domain.Entities;

public sealed class HabitEntry : BaseEntity
{ 
    public Guid HabitId { get; set; }
    public DateOnly Date { get; set; }
    public bool IsCompleted { get; set; } 

    public Habit Habit { get; set; } = default!;
}
