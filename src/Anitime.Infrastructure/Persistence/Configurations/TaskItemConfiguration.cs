using Anitime.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anitime.Infrastructure.Persistence.Configurations;

public sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.ToTable("tasks");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.Emoji).HasMaxLength(16).IsRequired();
        builder.Property(x => x.ClientRequestId).HasMaxLength(64);
        builder.HasIndex(x => new { x.UserId, x.Date });
        builder.HasIndex(x => new { x.UserId, x.ClientRequestId }).IsUnique();
        builder.HasOne(x => x.User).WithMany(x => x.Tasks).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
