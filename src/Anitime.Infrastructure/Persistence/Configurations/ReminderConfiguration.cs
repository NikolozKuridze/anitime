using Anitime.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anitime.Infrastructure.Persistence.Configurations;

public sealed class ReminderConfiguration : IEntityTypeConfiguration<Reminder>
{
    public void Configure(EntityTypeBuilder<Reminder> builder)
    {
        builder.ToTable("reminders");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Message).HasMaxLength(1000).IsRequired();
        builder.HasIndex(x => new { x.UserId, x.ScheduledForUtc });
        builder.HasIndex(x => new { x.IsEnabled, x.ScheduledForUtc });
        builder.HasOne(x => x.User).WithMany(x => x.Reminders).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
