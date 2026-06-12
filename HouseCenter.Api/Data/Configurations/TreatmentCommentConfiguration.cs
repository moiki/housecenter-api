using HouseCenter.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HouseCenter.Api.Data.Configurations;

public class TreatmentCommentConfiguration : IEntityTypeConfiguration<TreatmentComment>
{
    public void Configure(EntityTypeBuilder<TreatmentComment> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Body).IsRequired();

        builder.Property(c => c.Type)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(c => c.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.Treatment)
            .WithMany(t => t.Comments)
            .HasForeignKey(c => c.TreatmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
