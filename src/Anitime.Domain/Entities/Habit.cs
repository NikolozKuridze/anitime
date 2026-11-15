namespace Anitime.Domain.Entities;

public sealed class Habit : BaseEntity
{ 
    public Guid UserId { get; set; }
    public string Title { get; set; } = default!;
    public string Emoji { get; set; } = "🔥";
    public DateOnly StartDate { get; set; }
    public int DurationDays { get; set; } = 30; 

    /// <summary>Set by the app on create so a retried request returns this habit instead of making a second one.</summary>
    public string? ClientRequestId { get; set; }

    public User User { get; set; } = default!;
    public List<HabitEntry> Entries { get; set; } = [];
}
