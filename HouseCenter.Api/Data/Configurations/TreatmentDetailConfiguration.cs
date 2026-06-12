using HouseCenter.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HouseCenter.Api.Data.Configurations;

public class TreatmentDetailConfiguration : IEntityTypeConfiguration<TreatmentDetail>
{
    public void Configure(EntityTypeBuilder<TreatmentDetail> builder)
    {
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Name).IsRequired().HasMaxLength(200);
        builder.Property(d => d.Description).IsRequired();

        builder.HasOne(d => d.Treatment)
            .WithMany(t => t.Details)
            .HasForeignKey(d => d.TreatmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
