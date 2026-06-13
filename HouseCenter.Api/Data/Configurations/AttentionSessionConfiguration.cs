using HouseCenter.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HouseCenter.Api.Data.Configurations;

public class AttentionSessionConfiguration : IEntityTypeConfiguration<AttentionSession>
{
    public void Configure(EntityTypeBuilder<AttentionSession> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.AttentionType)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(s => s.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(s => s.Notes).HasMaxLength(2000);

        builder.HasOne(s => s.Patient)
            .WithMany()
            .HasForeignKey(s => s.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict: a collaborator with recorded sessions is operational history —
        // deleting the user must not erase the NGO's care records.
        builder.HasOne(s => s.Collaborator)
            .WithMany()
            .HasForeignKey(s => s.CollaboratorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Clinic)
            .WithMany()
            .HasForeignKey(s => s.ClinicId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(s => s.WorkRoute)
            .WithMany()
            .HasForeignKey(s => s.WorkRouteId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
