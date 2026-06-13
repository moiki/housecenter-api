using HouseCenter.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HouseCenter.Api.Data.Configurations;

public class PushSubscriptionConfiguration : IEntityTypeConfiguration<PushSubscription>
{
    public void Configure(EntityTypeBuilder<PushSubscription> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Token).IsRequired().HasMaxLength(512);

        builder.Property(s => s.Platform)
            .HasConversion<string>()
            .HasMaxLength(10);

        // A device token is globally unique to one install; registration upserts on it.
        builder.HasIndex(s => s.Token).IsUnique();

        builder.HasOne(s => s.Owner)
            .WithMany()
            .HasForeignKey(s => s.OwnerUserId)
            .OnDelete(DeleteBehavior.Cascade); // tokens are worthless once the user is gone
    }
}
