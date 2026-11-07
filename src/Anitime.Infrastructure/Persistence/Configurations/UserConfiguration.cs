using Anitime.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anitime.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Email).HasMaxLength(256).IsRequired();
        builder.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ProfileImageObjectName).HasMaxLength(512);
        builder.Property(x => x.ProfileImageContentType).HasMaxLength(128);
        builder.Property(x => x.BackgroundImageObjectName).HasMaxLength(512);
        builder.Property(x => x.BackgroundImageContentType).HasMaxLength(128);
        builder.HasIndex(x => x.Email).IsUnique();
    }
}
