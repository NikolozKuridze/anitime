using Anitime.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anitime.Infrastructure.Persistence.Configurations;

public sealed class ExerciseConfiguration : IEntityTypeConfiguration<Exercise>
{
    public void Configure(EntityTypeBuilder<Exercise> builder)
    {
        builder.ToTable("exercises");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Slug).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(60).IsRequired();
        builder.Property(x => x.Level).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Force).HasMaxLength(40);
        builder.Property(x => x.Mechanic).HasMaxLength(40);
        builder.Property(x => x.Equipment).HasMaxLength(80);
        builder.Property(x => x.YouTubeVideoId).HasMaxLength(20);

        // Npgsql maps List<string> to text[] natively; muscle filters use array containment.
        builder.Property(x => x.PrimaryMuscles).HasColumnType("text[]");
        builder.Property(x => x.SecondaryMuscles).HasColumnType("text[]");
        builder.Property(x => x.Instructions).HasColumnType("text[]");
        builder.Property(x => x.ImagePaths).HasColumnType("text[]");

        builder.HasIndex(x => x.Slug).IsUnique();
        builder.HasIndex(x => x.Category);
        builder.HasIndex(x => x.Equipment);
        builder.HasIndex(x => x.Level);
        builder.HasIndex(x => x.PrimaryMuscles).HasMethod("gin");
    }
}
