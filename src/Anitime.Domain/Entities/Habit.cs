namespace Anitime.Domain.Entities;

public sealed class Habit : BaseEntity
{ 
    public Guid UserId { get; set; }
    public string Title { get; set; } = default!;
    public string Emoji { get; set; } = "🔥";
    public DateOnly StartDate { get; set; }
    public int DurationDays { get; set; } = 30; 

    public User User { get; set; } = default!;
    public List<HabitEntry> Entries { get; set; } = [];
}
