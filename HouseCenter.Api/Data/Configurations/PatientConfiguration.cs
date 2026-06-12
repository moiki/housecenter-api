using HouseCenter.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HouseCenter.Api.Data.Configurations;

public class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.FirstName).IsRequired().HasMaxLength(100);
        builder.Property(p => p.LastName).IsRequired().HasMaxLength(100);
        builder.Property(p => p.Address).IsRequired().HasMaxLength(500);
        builder.Property(p => p.Country).HasMaxLength(100);
        builder.Property(p => p.State).HasMaxLength(100);
        builder.Property(p => p.City).HasMaxLength(100);

        builder.Property(p => p.Gender)
            .HasConversion<string>()
            .HasMaxLength(10);

        // HasDefaultValue does double duty: backfills existing rows with 'Medical'
        // when the migration adds the column, and keeps a DB-level default in sync
        // with the CLR default (spec R5).
        builder.Property(p => p.PrimaryAttentionType)
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(AttentionType.Medical);

        builder.HasOne(p => p.Clinic)
            .WithMany(c => c.Patients)
            .HasForeignKey(p => p.ClinicId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(p => p.WorkRoute)
            .WithMany(w => w.Patients)
            .HasForeignKey(p => p.WorkRouteId)
            .OnDelete(DeleteBehavior.SetNull);

        // Many-to-many: Patient <-> User (collaborators)
        builder.HasMany(p => p.Collaborators)
            .WithMany()
            .UsingEntity(j => j.ToTable("patient_collaborators"));

        // Many-to-many: Patient <-> User (remote doctors following this patient).
        // Explicit FK column names — EF would otherwise generate "Patient1Id" to
        // disambiguate from the Collaborators relationship above.
        builder.HasMany(p => p.AssignedDoctors)
            .WithMany()
            .UsingEntity<Dictionary<string, object>>(
                "patient_doctors",
                j => j.HasOne<User>().WithMany().HasForeignKey("DoctorId"),
                j => j.HasOne<Patient>().WithMany().HasForeignKey("PatientId"));
    }
}
