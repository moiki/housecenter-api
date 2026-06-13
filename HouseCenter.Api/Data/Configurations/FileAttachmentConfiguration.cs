using HouseCenter.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HouseCenter.Api.Data.Configurations;

public class FileAttachmentConfiguration : IEntityTypeConfiguration<FileAttachment>
{
    public void Configure(EntityTypeBuilder<FileAttachment> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.OwnerType)
            .HasConversion<string>()
            .HasMaxLength(40);

        builder.Property(a => a.StorageKey).IsRequired().HasMaxLength(512);
        builder.Property(a => a.FileName).IsRequired().HasMaxLength(255);
        builder.Property(a => a.ContentType).IsRequired().HasMaxLength(100);

        // Hot query is "all attachments for this owner".
        builder.HasIndex(a => new { a.OwnerType, a.OwnerId });

        // Restrict: an attachment is a record of who uploaded what — keep it if the user is removed.
        builder.HasOne(a => a.UploadedBy)
            .WithMany()
            .HasForeignKey(a => a.UploadedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
