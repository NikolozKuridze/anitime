using Anitime.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anitime.Infrastructure.Persistence.Configurations;

public sealed class ChecklistItemConfiguration : IEntityTypeConfiguration<ChecklistItem>
{
    public void Configure(EntityTypeBuilder<ChecklistItem> builder)
    {
        builder.ToTable("checklist_items");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(180).IsRequired();
        builder.HasIndex(x => x.TaskItemId);
        builder.HasOne(x => x.TaskItem).WithMany(x => x.ChecklistItems).HasForeignKey(x => x.TaskItemId).OnDelete(DeleteBehavior.Cascade);
    }
}
