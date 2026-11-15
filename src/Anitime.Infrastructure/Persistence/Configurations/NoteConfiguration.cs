using Anitime.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anitime.Infrastructure.Persistence.Configurations;

public sealed class NoteConfiguration : IEntityTypeConfiguration<Note>
{
    public void Configure(EntityTypeBuilder<Note> builder)
    {
        builder.ToTable("notes");
        builder.Property(x => x.ClientRequestId).HasMaxLength(64);
        builder.HasIndex(x => new { x.UserId, x.ClientRequestId }).IsUnique().HasFilter("\"IsDeleted\" = false");

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
        builder.Property(x => x.PaperStyle).HasMaxLength(16).IsRequired().HasDefaultValue(NotePaperStyles.Plain);
        builder.Property(x => x.Tags).IsRequired().HasDefaultValueSql("'{}'::text[]");
        builder.Property(x => x.ExplicitTags).IsRequired().HasDefaultValueSql("'{}'::text[]");
        builder.Property(x => x.LinkedNoteIds).IsRequired().HasDefaultValueSql("'{}'::uuid[]");
        builder.Property(x => x.AssetIds).IsRequired().HasDefaultValueSql("'{}'::uuid[]");
        builder.HasOne<NoteFolder>().WithMany().HasForeignKey(x => x.FolderId).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => new { x.UserId, x.FolderId });
        builder.HasIndex(x => new { x.IsDeleted, x.DeletedAtUtc });
        builder.HasIndex(x => x.Tags).HasMethod("gin");
        builder.HasIndex(x => x.LinkedNoteIds).HasMethod("gin");
        builder.HasIndex(x => x.AssetIds).HasMethod("gin");
        builder.Property(x => x.SearchText).IsRequired().HasDefaultValue(string.Empty);
        // Search: narrows to one user's live notes newest-first, then a byte-wise LIKE on SearchText; stops at the limit.
        builder.HasIndex(x => new { x.UserId, x.IsDeleted, x.UpdatedAtUtc });
    }
}
