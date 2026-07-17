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

        // One-to-one: Collaborator -> User (field profile link). Nullable FK — a Collaborator
        // may be a legacy/roster-only row with no linked User (see Domain/Entities/Collaborator.cs).
        // Deleting the User must not cascade-delete the roster row — it just unlinks it.
        builder.HasOne(e => e.User)
            .WithOne(u => u.Collaborator)
            .HasForeignKey<Collaborator>(e => e.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        // Partial-unique: at most one Collaborator per User, but many Collaborators may have
        // UserId = null (legacy rows pending backfill/reconciliation). See
        // openspec/changes/collaborator-user-link/design.md Decision 3.
        // Email unique index is deferred to Phase 3 (Migration B, post-reconciliation).
        builder.HasIndex(e => e.UserId)
            .IsUnique()
            .HasFilter("\"UserId\" IS NOT NULL");

        // One-to-many: Employee -> Positions
        builder.HasMany(e => e.Positions)
            .WithOne(p => p.Collaborator)
            .HasForeignKey(p => p.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
