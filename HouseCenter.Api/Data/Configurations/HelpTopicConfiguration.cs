using HouseCenter.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HouseCenter.Api.Data.Configurations;

public class HelpTopicConfiguration : IEntityTypeConfiguration<HelpTopic>
{
    public void Configure(EntityTypeBuilder<HelpTopic> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.TopicKey).IsRequired().HasMaxLength(120);
        builder.Property(t => t.Title).IsRequired().HasMaxLength(150);
        builder.Property(t => t.Summary).IsRequired().HasMaxLength(300);
        builder.Property(t => t.Body).IsRequired().HasMaxLength(20000);
        builder.Property(t => t.Category).IsRequired().HasMaxLength(60);

        builder.Property(t => t.Audience)
            .HasConversion<string>()
            .HasMaxLength(30);

        // TopicKey is a stable frontend deep-link contract; unique across every row,
        // including soft-deleted ones (this index is not filtered, unlike LINQ queries
        // through the global IsActive filter). The create handler (Phase 2) re-checks
        // this via IgnoreQueryFilters() to return a friendly 409 instead of a raw DB error.
        builder.HasIndex(t => t.TopicKey).IsUnique();
    }
}
