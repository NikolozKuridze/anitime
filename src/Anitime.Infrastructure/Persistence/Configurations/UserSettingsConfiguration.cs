using Anitime.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anitime.Infrastructure.Persistence.Configurations;

public sealed class UserSettingsConfiguration : IEntityTypeConfiguration<UserSettings>
{
    public void Configure(EntityTypeBuilder<UserSettings> builder)
    {
        builder.ToTable("user_settings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.BackgroundPrimaryColor).HasMaxLength(32);
        builder.Property(x => x.BackgroundSecondaryColor).HasMaxLength(32);
        builder.Property(x => x.AccentColor).HasMaxLength(32);
        builder.Property(x => x.Locale).HasMaxLength(16);
        builder.Property(x => x.TimeZone).HasMaxLength(64);
        builder.HasIndex(x => x.UserId).IsUnique();
        builder.HasOne(x => x.User).WithOne(x => x.Settings).HasForeignKey<UserSettings>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
