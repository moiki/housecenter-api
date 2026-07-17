using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Constants;
using HouseCenter.Api.Domain.Entities;
using HouseCenter.Api.Features.Auth;
using HouseCenter.Api.Features.Clinics;
using HouseCenter.Api.Features.Collaborators;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HouseCenter.Api.Tests;

// Integration tests for POST /collaborators/reconcile (Phase 3 of collaborator-user-link).
// Runs in ALL environments (unlike POST /seed, which is Dev/Staging-only) — see
// tasks.md "Decisions Resolved" #2. Because the [Collection("Api")] fixture is SHARED
// across every test class in the collection (one Postgres DB, recreated once per test run,
// not per class), assertions here use Contain/GreaterThanOrEqualTo + direct DB lookups by
// the IDs THIS test created, never exact global counts/lists — other tests in the
// collection may leave their own unlinked Users/Collaborators behind.
[Collection("Api")]
public sealed class CollaboratorReconciliationTests(ApiFactory factory)
{
    private const string ReconcilePath = "/collaborators/reconcile";

    [Fact]
    public async Task Reconcile_WithoutAuth_Returns401()
    {
        var response = await factory.CreateClient().PostAsync(ReconcilePath, null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Reconcile_AsMember_Returns403()
    {
        var email = $"member-reconcile-{Guid.NewGuid():N}@test.local";
        await InsertUserAsync(email, "Member123!test");
        var client = await LoginAsAsync(email, "Member123!test");

        var response = await client.PostAsync(ReconcilePath, null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reconcile_LinksMatchingUnlinkedPair_CaseInsensitively()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var clinic = await CreateClinicAsync(admin);
        var uniquePart = Guid.NewGuid().ToString("N");
        var userEmail = $"reconcile-{uniquePart}@test.local";
        var collaboratorEmail = $"Reconcile-{uniquePart}@Test.Local"; // different casing

        var userId = await InsertUserAsync(userEmail, "Member123!test");
        var collaboratorId = await InsertUnlinkedCollaboratorAsync(clinic.Id, collaboratorEmail);

        var response = await admin.PostAsync(ReconcilePath, null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ReconciliationResponse>();
        result!.MatchedCount.Should().BeGreaterThanOrEqualTo(1);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HouseCenterDbContext>();
        var collaborator = await db.Collaborators.SingleAsync(c => c.Id == collaboratorId);
        collaborator.UserId.Should().Be(userId);
        collaborator.Email.Should().Be(collaboratorEmail.ToLowerInvariant()); // normalized in place
    }

    [Fact]
    public async Task Reconcile_IsIdempotent_SecondCallDoesNotUnlinkOrRematch()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var clinic = await CreateClinicAsync(admin);
        var email = $"idempotent-{Guid.NewGuid():N}@test.local";
        var userId = await InsertUserAsync(email, "Member123!test");
        var collaboratorId = await InsertUnlinkedCollaboratorAsync(clinic.Id, email);

        await admin.PostAsync(ReconcilePath, null);
        var second = await admin.PostAsync(ReconcilePath, null);

        second.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HouseCenterDbContext>();
        var collaborator = await db.Collaborators.SingleAsync(c => c.Id == collaboratorId);
        collaborator.UserId.Should().Be(userId); // unchanged, not unlinked/re-linked
    }

    [Fact]
    public async Task Reconcile_ReportsUnlinkedMemberUser_WithNoMatchingCollaborator()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var email = $"gap-{Guid.NewGuid():N}@test.local";
        var userId = await InsertUserAsync(email, "Member123!test");

        var response = await admin.PostAsync(ReconcilePath, null);

        var result = await response.Content.ReadFromJsonAsync<ReconciliationResponse>();
        result!.UnlinkedMemberUsers.Should().Contain(u => u.Id == userId && u.Email == email);
    }

    [Fact]
    public async Task Reconcile_LeavesCollaboratorWithNoMatchingUser_AsLegacy_StillVisible()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var clinic = await CreateClinicAsync(admin);
        var email = $"legacy-{Guid.NewGuid():N}@test.local";
        var collaboratorId = await InsertUnlinkedCollaboratorAsync(clinic.Id, email);

        await admin.PostAsync(ReconcilePath, null);

        var getResponse = await admin.GetAsync($"/collaborators/{collaboratorId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var collaborator = await getResponse.Content.ReadFromJsonAsync<CollaboratorResponse>();
        collaborator!.UserId.Should().BeNull();
        collaborator.IsLinked.Should().BeFalse();
        collaborator.IsActive.Should().BeTrue(); // legacy is retained, never hidden or deactivated
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static async Task<ClinicResponse> CreateClinicAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/clinics",
            new CreateClinicRequest($"Clínica {Guid.NewGuid():N}"[..25], "Calle Central"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ClinicResponse>())!;
    }

    /// <summary>Direct DbContext insert — models a Collaborator that predates this change or
    /// was rostered before its matching invitee ever signed up (Decision 2's real-data
    /// backfill scenario), bypassing CollaboratorService's own resolution/dup-check.</summary>
    private async Task<Guid> InsertUnlinkedCollaboratorAsync(Guid clinicId, string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HouseCenterDbContext>();

        var collaborator = new Collaborator
        {
            FirstName = "Reconcile",
            LastName = "Test",
            Email = email,
            PhoneNumber = "8888-0000",
            Address = "Dirección",
            ClinicId = clinicId
        };

        db.Collaborators.Add(collaborator);
        await db.SaveChangesAsync();

        return collaborator.Id;
    }

    private async Task<Guid> InsertUserAsync(string email, string password, string roleName = RoleNames.Member)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HouseCenterDbContext>();
        var role = await db.Roles.SingleAsync(r => r.Name == roleName);

        var user = new User
        {
            FirstName = "Test",
            LastName = "User",
            Email = email,
            HashedPassword = BCrypt.Net.BCrypt.HashPassword(password),
            Address = "Test 123",
            EmailConfirmed = true,
            Roles = [role]
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return user.Id;
    }

    private async Task<HttpClient> LoginAsAsync(string email, string password)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/auth/login",
            new LoginRequest(email, password, $"test-device-{Guid.NewGuid():N}", null, DevicePlatform.Web));
        response.EnsureSuccessStatusCode();

        var tokens = await response.Content.ReadFromJsonAsync<TokenPairResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        return client;
    }
}
