using HouseCenter.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HouseCenter.Api.Data.Configurations;

public class EmployeeConfiguration : IEntityTypeConfiguration<Collaborator>
{
    public void Configure(EntityTypeBuilder<Collaborator> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.FirstName).IsRequired().HasMaxLength(100);
        builder.Property(e => e.LastName).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Email).IsRequired().HasMaxLength(300);
        builder.Property(e => e.PhoneNumber).IsRequired().HasMaxLength(50);
        builder.Property(e => e.Address).IsRequired().HasMaxLength(500);
        builder.Property(e => e.Country).HasMaxLength(100);
        builder.Property(e => e.State).HasMaxLength(100);
        builder.Property(e => e.City).HasMaxLength(100);

        builder.HasOne(e => e.Clinic)
            .WithMany(c => c.Collaborators)
            .HasForeignKey(e => e.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.WorkRoute)
            .WithMany(w => w.Collaborators)
            .HasForeignKey(e => e.WorkRouteId)
            .OnDelete(DeleteBehavior.SetNull);

        // One-to-many: Employee -> Positions
        builder.HasMany(e => e.Positions)
            .WithOne(p => p.Collaborator)
            .HasForeignKey(p => p.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
