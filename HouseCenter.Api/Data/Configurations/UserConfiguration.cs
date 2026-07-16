using HouseCenter.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HouseCenter.Api.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Email)
            .IsRequired()
            .HasMaxLength(300);

        builder.HasIndex(u => u.Email)
            .IsUnique();

        builder.Property(u => u.FirstName).IsRequired().HasMaxLength(100);
        builder.Property(u => u.LastName).IsRequired().HasMaxLength(100);
        builder.Property(u => u.HashedPassword).IsRequired();
        builder.Property(u => u.Address).IsRequired().HasMaxLength(500);
        builder.Property(u => u.PhoneNumber).HasMaxLength(50);
        builder.Property(u => u.Country).HasMaxLength(100);
        builder.Property(u => u.State).HasMaxLength(100);
        builder.Property(u => u.City).HasMaxLength(100);
        builder.Property(u => u.Profession).HasMaxLength(100);

        // HasDefaultValue backfills existing rows with 'Es' when the migration adds the
        // column (matches PrimaryAttentionType/Gender's precedent) and keeps a DB-level
        // default in sync with the CLR default.
        builder.Property(u => u.Language)
            .HasConversion<string>()
            .HasMaxLength(10)
            .HasDefaultValue(Language.Es);

        // Many-to-many: User <-> Role
        builder.HasMany(u => u.Roles)
            .WithMany()
            .UsingEntity(j => j.ToTable("user_roles"));
    }
}
