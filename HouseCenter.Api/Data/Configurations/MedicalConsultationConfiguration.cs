using HouseCenter.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HouseCenter.Api.Data.Configurations;

public class MedicalConsultationConfiguration : IEntityTypeConfiguration<MedicalConsultation>
{
    public void Configure(EntityTypeBuilder<MedicalConsultation> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Title).IsRequired().HasMaxLength(300);

        builder.Property(c => c.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.HasOne(c => c.Patient)
            .WithMany()
            .HasForeignKey(c => c.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.Treatment)
            .WithMany()
            .HasForeignKey(c => c.TreatmentId)
            .OnDelete(DeleteBehavior.SetNull);

        // Restrict on both user FKs: a consultation thread is clinical history —
        // deleting a user must never erase it.
        builder.HasOne(c => c.OpenedBy)
            .WithMany()
            .HasForeignKey(c => c.OpenedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.AssignedDoctor)
            .WithMany()
            .HasForeignKey(c => c.AssignedDoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        // Aggregate root: messages live and die with their consultation. In normal
        // operation deletion is the global soft-delete (IsActive on the root hides
        // the thread); the DB cascade is only a safety net for physical deletes.
        builder.HasMany(c => c.Messages)
            .WithOne(m => m.Consultation)
            .HasForeignKey(m => m.ConsultationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
