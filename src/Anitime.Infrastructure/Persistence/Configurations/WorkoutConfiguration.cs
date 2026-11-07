using Anitime.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anitime.Infrastructure.Persistence.Configurations;

public sealed class WorkoutConfiguration : IEntityTypeConfiguration<Workout>
{
    public void Configure(EntityTypeBuilder<Workout> builder)
    {
        builder.ToTable("workouts");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Slug).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(160).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(600).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(60).IsRequired();
        builder.Property(x => x.Level).HasMaxLength(40).IsRequired();
        builder.Property(x => x.YouTubeVideoId).HasMaxLength(20).IsRequired();
        builder.Property(x => x.TargetMuscles).HasColumnType("text[]");

        builder.HasIndex(x => x.Slug).IsUnique();
        builder.HasIndex(x => x.Category);
    }
}
