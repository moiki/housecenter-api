using FluentAssertions;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Constants;
using HouseCenter.Api.Domain.Entities;
using HouseCenter.Api.Features.Collaborators;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HouseCenter.Api.Tests;

// Unit tests for CollaboratorReconciliationService (Phase 3 of collaborator-user-link,
// design.md Decision 2's match/gap algorithm). InMemory EF provider — no HTTP, no
// WebApplicationFactory. DB-level unique-index enforcement for Migration B
// (AddCollaboratorEmailUnique) is a SEPARATE Postgres integration test — the InMemory
// provider does not enforce unique-index constraints (see CollaboratorUserLinkTests' PR1
// precedent).
public sealed class CollaboratorReconciliationServiceTests : IDisposable
{
    private readonly HouseCenterDbContext _db;
    private readonly CollaboratorReconciliationService _sut;

    public CollaboratorReconciliationServiceTests()
    {
        var options = new DbContextOptionsBuilder<HouseCenterDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()) // unique DB per test
            .Options;

        _db = new HouseCenterDbContext(options);
        _sut = new CollaboratorReconciliationService(_db, NullLogger<CollaboratorReconciliationService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private static Clinic NewClinic() => new() { Name = "Clínica Test", Address = "Dirección" };

    private static Role NewRole(string name) => new() { Name = name };

    private static User NewUser(string email, Role role) => new()
    {
        FirstName = "Nombre",
        LastName = "Apellido",
        Address = "Dirección",
        Email = email,
        HashedPassword = "hash",
        Roles = [role]
    };

    private static Collaborator NewCollaborator(Clinic clinic, string email, Guid? userId = null) => new()
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

    [Fact]
    public async Task ReconcileAsync_MatchesUnlinkedCollaboratorToUnlinkedMemberUser_ByCaseInsensitiveEmail()
    {
        var role = NewRole(RoleNames.Member);
        var clinic = NewClinic();
        var user = NewUser("Match@Test.Com", role);
        var collaborator = NewCollaborator(clinic, "match@test.com");

        _db.AddRange(role, clinic, user, collaborator);
        await _db.SaveChangesAsync();

        var result = await _sut.ReconcileAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.MatchedCount.Should().Be(1);
        (await _db.Collaborators.SingleAsync(c => c.Id == collaborator.Id)).UserId.Should().Be(user.Id);
    }

    [Fact]
    public async Task ReconcileAsync_NormalizesCollaboratorEmailToLowercase()
    {
        var role = NewRole(RoleNames.Member);
        var clinic = NewClinic();
        var user = NewUser("norm@test.com", role);
        var collaborator = NewCollaborator(clinic, "NORM@TEST.COM");

        _db.AddRange(role, clinic, user, collaborator);
        await _db.SaveChangesAsync();

        await _sut.ReconcileAsync(CancellationToken.None);

        (await _db.Collaborators.SingleAsync(c => c.Id == collaborator.Id)).Email.Should().Be("norm@test.com");
    }

    [Fact]
    public async Task ReconcileAsync_SkipsAlreadyLinkedCollaborator()
    {
        var role = NewRole(RoleNames.Member);
        var clinic = NewClinic();
        var linkedUser = NewUser("already@test.com", role);
        var linkedCollaborator = NewCollaborator(clinic, "already@test.com", linkedUser.Id);

        _db.AddRange(role, clinic, linkedUser, linkedCollaborator);
        await _db.SaveChangesAsync();

        var result = await _sut.ReconcileAsync(CancellationToken.None);

        result.Value.MatchedCount.Should().Be(0); // nothing NEW to match
        result.Value.UnlinkedMemberUsers.Should().BeEmpty(); // the User is already linked
    }

    [Fact]
    public async Task ReconcileAsync_CollaboratorWithNoMatch_StaysLegacy_NotReported()
    {
        var clinic = NewClinic();
        var legacyCollaborator = NewCollaborator(clinic, "legacy@test.com");

        _db.AddRange(clinic, legacyCollaborator);
        await _db.SaveChangesAsync();

        var result = await _sut.ReconcileAsync(CancellationToken.None);

        result.Value.MatchedCount.Should().Be(0);
        (await _db.Collaborators.SingleAsync(c => c.Id == legacyCollaborator.Id)).UserId.Should().BeNull();
        // Design.md Decision 5: a legacy Collaborator is a valid permanent state, not an
        // anomaly — it must never appear in the gap-report list (that list is Member-Users
        // only).
    }

    [Fact]
    public async Task ReconcileAsync_MemberUserWithNoMatch_IsReportedAsGap()
    {
        var role = NewRole(RoleNames.Member);
        var user = NewUser("gap@test.com", role);

        _db.AddRange(role, user);
        await _db.SaveChangesAsync();

        var result = await _sut.ReconcileAsync(CancellationToken.None);

        result.Value.UnlinkedMemberUsers.Should()
            .ContainSingle(u => u.Id == user.Id && u.Email == "gap@test.com");
    }

    [Fact]
    public async Task ReconcileAsync_NonMemberUserWithNoCollaborator_IsNotReported()
    {
        var role = NewRole(RoleNames.Doctor);
        var doctor = NewUser("doctor@test.com", role);

        _db.AddRange(role, doctor);
        await _db.SaveChangesAsync();

        var result = await _sut.ReconcileAsync(CancellationToken.None);

        result.Value.UnlinkedMemberUsers.Should().BeEmpty();
    }

    [Fact]
    public async Task ReconcileAsync_IsIdempotent_SecondRunMatchesZeroForAlreadyReconciledPair()
    {
        var role = NewRole(RoleNames.Member);
        var clinic = NewClinic();
        var user = NewUser("idempotent@test.com", role);
        var collaborator = NewCollaborator(clinic, "idempotent@test.com");

        _db.AddRange(role, clinic, user, collaborator);
        await _db.SaveChangesAsync();

        var first = await _sut.ReconcileAsync(CancellationToken.None);
        first.Value.MatchedCount.Should().Be(1);

        var second = await _sut.ReconcileAsync(CancellationToken.None);
        second.Value.MatchedCount.Should().Be(0);

        (await _db.Collaborators.SingleAsync(c => c.Id == collaborator.Id)).UserId.Should().Be(user.Id);
    }

    [Fact]
    public async Task ReconcileAsync_TwoCollaboratorsShareEmail_OnlyOneClaimsTheUser()
    {
        // A pre-existing duplicate-email scenario (both unlinked) — reconciliation must
        // resolve one-to-one and leave the loser legacy, never double-link a User. This is
        // exactly the case Migration B (AddCollaboratorEmailUnique) is designed to still
        // reject afterward, since one Collaborator row remains a true duplicate.
        var role = NewRole(RoleNames.Member);
        var clinic = NewClinic();
        var user = NewUser("shared@test.com", role);
        var first = NewCollaborator(clinic, "shared@test.com");
        var second = NewCollaborator(clinic, "shared@test.com");

        _db.AddRange(role, clinic, user, first, second);
        await _db.SaveChangesAsync();

        var result = await _sut.ReconcileAsync(CancellationToken.None);

        result.Value.MatchedCount.Should().Be(1);
        var reloadedFirst = await _db.Collaborators.SingleAsync(c => c.Id == first.Id);
        var reloadedSecond = await _db.Collaborators.SingleAsync(c => c.Id == second.Id);
        new[] { reloadedFirst.UserId, reloadedSecond.UserId }.Should().ContainSingle(id => id == user.Id);
        new[] { reloadedFirst.UserId, reloadedSecond.UserId }.Should().Contain(id => id == null);
    }
}
