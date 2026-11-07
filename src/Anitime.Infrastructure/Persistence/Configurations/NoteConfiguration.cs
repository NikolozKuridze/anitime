using Anitime.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anitime.Infrastructure.Persistence.Configurations;

public sealed class NoteConfiguration : IEntityTypeConfiguration<Note>
{
    public void Configure(EntityTypeBuilder<Note> builder)
    {
        builder.ToTable("notes");

        builder.Property(x => x.Title).HasMaxLength(160).IsRequired();
        builder.Property(x => x.ContentJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.PlainTextPreview).HasMaxLength(50_000).IsRequired();
        builder.Property(x => x.FontFamily).HasMaxLength(64).IsRequired();
        builder.Property(x => x.TextColor).HasMaxLength(32).IsRequired();
        builder.Property(x => x.BackgroundColor).HasMaxLength(32).IsRequired();
        builder.Property(x => x.VersionToken).HasMaxLength(64).IsRequired().IsConcurrencyToken();

        builder.HasIndex(x => new { x.UserId, x.IsPinned, x.UpdatedAtUtc });
        builder.HasIndex(x => new { x.UserId, x.IsArchived, x.UpdatedAtUtc });
        builder.HasIndex(x => new { x.UserId, x.VersionToken });
    }
}
