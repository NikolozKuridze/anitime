using Anitime.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anitime.Infrastructure.Persistence.Configurations;

public sealed class NoteContentBackupConfiguration : IEntityTypeConfiguration<NoteContentBackup>
{
    public void Configure(EntityTypeBuilder<NoteContentBackup> builder)
    {
        builder.ToTable("note_content_backups");

        builder.Property(x => x.ContentJson).HasColumnType("jsonb").IsRequired();

        builder.HasOne<Note>()
            .WithMany()
            .HasForeignKey(x => x.NoteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.UserId, x.NoteId });
        // For a future retention job (the plan keeps backups 90 days).
        builder.HasIndex(x => x.CreatedAtUtc);
    }
}
