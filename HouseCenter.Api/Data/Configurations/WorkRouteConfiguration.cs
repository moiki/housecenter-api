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

        builder.HasOne(w => w.Clinic)
            .WithMany(c => c.WorkRoutes)
            .HasForeignKey(w => w.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);

        // [Flags] enum stored as a single comma-joined string (e.g. "Monday, Thursday").
        // HasDefaultValue does double duty (see PatientConfiguration.PrimaryAttentionType
        // precedent): backfills existing rows when the migration adds the column, and keeps
        // a DB-level default in sync with the CLR default.
        builder.Property(w => w.RecurrenceDays)
            .HasConversion<string>()
            .HasMaxLength(80)
            .HasDefaultValue(Weekday.None);

        builder.Property(w => w.IsRecurrenceIndefinite)
            .HasDefaultValue(true);

        // DateOnly columns need no extra config — they sidestep the DbContext's global
        // UTC-DateTime converter (which only matches DateTime/DateTime?).
    }
}
