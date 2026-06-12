using HouseCenter.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HouseCenter.Api.Data.Configurations;

public class WorkRouteConfiguration : IEntityTypeConfiguration<WorkRoute>
{
    public void Configure(EntityTypeBuilder<WorkRoute> builder)
    {
        builder.HasKey(w => w.Id);

        builder.Property(w => w.RouteName).IsRequired().HasMaxLength(200);
        builder.Property(w => w.Description).IsRequired();
        builder.Property(w => w.FeaturedImage).HasMaxLength(500);

        // Owned entity: DestinationPoint lives as JSON column (no separate table)
        builder.OwnsMany(w => w.Destinations, d =>
        {
            d.ToJson();
            d.Property(x => x.Name).IsRequired().HasMaxLength(200);
            d.Property(x => x.Description).IsRequired();
            d.Property(x => x.Picture).HasMaxLength(500);
            d.Property(x => x.GoogleMapUrl).HasMaxLength(500);
        });

        builder.HasOne(w => w.Clinic)
            .WithMany(c => c.WorkRoutes)
            .HasForeignKey(w => w.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
