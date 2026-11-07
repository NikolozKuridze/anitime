using Anitime.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anitime.Infrastructure.Persistence.Configurations;

public sealed class HabitConfiguration : IEntityTypeConfiguration<Habit>
{
    public void Configure(EntityTypeBuilder<Habit> builder)
    {
        builder.ToTable("habits");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Emoji).HasMaxLength(16).IsRequired();
        builder.HasIndex(x => new { x.UserId, x.StartDate });
        builder.HasOne(x => x.User).WithMany(x => x.Habits).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
