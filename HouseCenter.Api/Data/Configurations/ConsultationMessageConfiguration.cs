using HouseCenter.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HouseCenter.Api.Data.Configurations;

public class ConsultationMessageConfiguration : IEntityTypeConfiguration<ConsultationMessage>
{
    public void Configure(EntityTypeBuilder<ConsultationMessage> builder)
    {
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Body).IsRequired().HasMaxLength(5000);
        builder.Property(m => m.AttachmentUrl).HasMaxLength(500);

        builder.HasOne(m => m.Author)
            .WithMany()
            .HasForeignKey(m => m.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
