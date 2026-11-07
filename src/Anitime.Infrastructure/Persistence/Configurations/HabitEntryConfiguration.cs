using Anitime.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anitime.Infrastructure.Persistence.Configurations;

public sealed class HabitEntryConfiguration : IEntityTypeConfiguration<HabitEntry>
{
    public void Configure(EntityTypeBuilder<HabitEntry> builder)
    {
        builder.ToTable("habit_entries");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.HabitId, x.Date }).IsUnique();
        builder.HasOne(x => x.Habit).WithMany(x => x.Entries).HasForeignKey(x => x.HabitId).OnDelete(DeleteBehavior.Cascade);
    }
}
