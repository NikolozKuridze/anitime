using Anitime.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anitime.Infrastructure.Persistence.Configurations;

public sealed class NoteFolderConfiguration : IEntityTypeConfiguration<NoteFolder>
{
    public void Configure(EntityTypeBuilder<NoteFolder> builder)
    {
        builder.ToTable("note_folders");
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<NoteFolder>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(x => new { x.UserId, x.ParentId });
    }
}
