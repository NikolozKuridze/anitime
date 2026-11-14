using Anitime.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anitime.Infrastructure.Persistence.Configurations;

public sealed class SmartFolderConfiguration : IEntityTypeConfiguration<SmartFolder>
{
    public void Configure(EntityTypeBuilder<SmartFolder> builder)
    {
        builder.ToTable("smart_folders");
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.FilterJson).HasColumnType("jsonb").IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.UserId);
    }
}
