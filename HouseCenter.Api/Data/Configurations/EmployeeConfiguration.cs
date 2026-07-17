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
        builder.HasIndex(e => e.UserId)
            .IsUnique()
            .HasFilter("\"UserId\" IS NOT NULL");

        // Phase 3 (Migration B, AddCollaboratorEmailUnique): plain unique index, mirroring
        // User.Email (UserConfiguration) exactly — no partial filter, so uniqueness applies
        // regardless of IsActive, same as User.Email today. Applied ONLY after
        // CollaboratorReconciliationService.ReconcileAsync has run and confirmed zero
        // duplicate emails; the migration is designed to fail loudly on any remaining
        // duplicate as an intentional safety gate, not a bug to work around. See design.md
        // Decision 3.
        builder.HasIndex(e => e.Email)
            .IsUnique();

        // One-to-many: Employee -> Positions
        builder.HasMany(e => e.Positions)
            .WithOne(p => p.Collaborator)
            .HasForeignKey(p => p.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
