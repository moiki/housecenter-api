using HouseCenter.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HouseCenter.Api.Data.Configurations;

public class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.DeviceId).IsRequired().HasMaxLength(64);
        builder.Property(s => s.DeviceName).HasMaxLength(200);

        builder.Property(s => s.CurrentTokenHash).IsRequired().HasMaxLength(64);
        builder.Property(s => s.PreviousTokenHash).HasMaxLength(64);

        builder.Property(s => s.Platform)
            .HasConversion<string>()
            .HasMaxLength(10);

        builder.Property(s => s.RevokedReason)
            .HasConversion<string>()
            .HasMaxLength(20);

        // Uniqueness scoped to ACTIVE rows: revoked rows keep their hash for audit, so a
        // global unique index would be semantically wrong (SHA-256 collisions aside).
        builder.HasIndex(s => s.CurrentTokenHash)
            .IsUnique()
            .HasFilter("\"IsActive\" = true");

        builder.HasIndex(s => s.PreviousTokenHash); // reuse-detection lookup
        builder.HasIndex(s => s.UserId);            // GET /auth/sessions
        builder.HasIndex(s => new { s.UserId, s.DeviceId }); // login-time upsert

        builder.HasOne(s => s.User)
            .WithMany(u => u.Sessions)
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade); // sessions are worthless once the user is gone
    }
}
