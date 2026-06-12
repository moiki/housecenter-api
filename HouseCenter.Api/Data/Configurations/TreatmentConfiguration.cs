using HouseCenter.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HouseCenter.Api.Data.Configurations;

public class TreatmentConfiguration : IEntityTypeConfiguration<Treatment>
{
    public void Configure(EntityTypeBuilder<Treatment> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).IsRequired().HasMaxLength(200);
        builder.Property(t => t.Description).IsRequired();

        builder.Property(t => t.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(t => t.Type)
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(AttentionType.Medical);

        builder.HasOne(t => t.Patient)
            .WithMany(p => p.Treatments)
            .HasForeignKey(t => t.PatientId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
