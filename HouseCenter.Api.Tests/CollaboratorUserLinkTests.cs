using FluentAssertions;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace HouseCenter.Api.Tests;

// Unit tests for the Collaborator <-> User 1:1 link declared in EmployeeConfiguration
// (Phase 1 of collaborator-user-link, schema foundation). InMemory EF provider — no HTTP,
// no WebApplicationFactory.
//
// NOTE: the InMemory provider does NOT enforce relational unique-index constraints at
// SaveChangesAsync time — verified empirically: even a plain, non-filtered, non-nullable
// unique index (UserConfiguration's `HasIndex(u => u.Email).IsUnique()`) allows a duplicate
// insert to succeed silently under InMemory. That enforcement only exists in the real
// Postgres provider (and is what the generated migration's unique index enforces in
// production). So the two tests below split accordingly:
//   - Metadata tests assert EmployeeConfiguration DECLARES the partial-unique index and the
//     SetNull delete behavior correctly (this is what a schema-config unit test CAN verify
//     under InMemory, and it is what would catch a misconfigured filter/behavior).
//   - A behavioral SaveChangesAsync test asserts that multiple Collaborators with
//     UserId = null save without error (the FK/nav wiring does not choke on nulls).
public sealed class CollaboratorUserLinkTests : IDisposable
{
    private readonly HouseCenterDbContext _db;

    public CollaboratorUserLinkTests()
    {
        var options = new DbContextOptionsBuilder<HouseCenterDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()) // unique DB per test
            .Options;

        _db = new HouseCenterDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    private static Clinic NewClinic() => new() { Name = "Clínica Test", Address = "Dirección", IsActive = true };

    private static Collaborator NewCollaborator(Clinic clinic, string email, Guid? userId) => new()
    {
        FirstName = "Nombre",
        LastName = "Apellido",
        Address = "Dirección",
        Email = email,
        PhoneNumber = "00000000",
        ClinicId = clinic.Id,
        Clinic = clinic,
        UserId = userId
    };

    private IIndex GetUserIdIndex() =>
        _db.Model.FindEntityType(typeof(Collaborator))!
            .GetIndexes()
            .Single(i => i.Properties.Count == 1 && i.Properties[0].Name == nameof(Collaborator.UserId));

    [Fact]
    public void UserIdIndex_IsUnique_WithPartialNullFilter()
    {
        // Proves EmployeeConfiguration declares the partial-unique index exactly as design.md
        // Decision 3 specifies: unique on UserId, filtered so NULLs are excluded — allowing
        // many legacy (unlinked) Collaborators while still enforcing at most one Collaborator
        // per User for non-null values.
        var index = GetUserIdIndex();

        index.IsUnique.Should().BeTrue();
        index.GetFilter().Should().Be("\"UserId\" IS NOT NULL");
    }

    [Fact]
    public void CollaboratorUserForeignKey_IsOneToOne_WithSetNullOnDelete()
    {
        // Proves the FK is configured as a 1:1 dependent (Collaborator) -> principal (User)
        // relationship with OnDelete: SetNull — deleting a User must unlink the roster row,
        // not cascade-delete or block deletion.
        var foreignKey = _db.Model.FindEntityType(typeof(Collaborator))!
            .GetForeignKeys()
            .Single(fk => fk.PrincipalEntityType.ClrType == typeof(User));

        foreignKey.IsUnique.Should().BeTrue();
        foreignKey.DeleteBehavior.Should().Be(DeleteBehavior.SetNull);
        foreignKey.Properties.Should().ContainSingle(p => p.Name == nameof(Collaborator.UserId));
    }

    [Fact]
    public async Task MultipleCollaborators_WithNullUserId_SaveWithoutError()
    {
        var clinic = NewClinic();
        _db.Set<Clinic>().Add(clinic);
        _db.Set<Collaborator>().AddRange(
            NewCollaborator(clinic, "legacy1@test.com", null),
            NewCollaborator(clinic, "legacy2@test.com", null));

        // Two unlinked (UserId == null) collaborators must save cleanly — the nullable FK and
        // partial-unique index must not reject NULLs.
        var act = async () => await _db.SaveChangesAsync();
        await act.Should().NotThrowAsync();

        (await _db.Set<Collaborator>().CountAsync(c => c.UserId == null)).Should().Be(2);
    }
}
