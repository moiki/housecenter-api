using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using HouseCenter.Api.Common.Pagination;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Constants;
using HouseCenter.Api.Domain.Entities;
using HouseCenter.Api.Features.Auth;
using HouseCenter.Api.Features.Clinics;
using HouseCenter.Api.Features.Collaborators;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HouseCenter.Api.Tests;

[Collection("Api")]
public sealed class CollaboratorCrudTests(ApiFactory factory)
{
    // Collaborators stays UNVERSIONED — only Clinics is under /api/v1/.
    private const string Base = "/collaborators";

    private Task<HttpClient> Client() => factory.CreateAuthenticatedClientAsync();

    [Fact]
    public async Task CreateCollaborator_WithValidData_Returns201AndPersistedCollaborator()
    {
        var client = await Client();
        var clinic = await CreateClinicAsync(client);

        var response = await PostCollaboratorAsync(client, clinic.Id, "Ana", "Gómez");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var collaborator = await response.Content.ReadFromJsonAsync<CollaboratorResponse>();
        collaborator!.FirstName.Should().Be("Ana");
        collaborator.ClinicId.Should().Be(clinic.Id);
        collaborator.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task CreateCollaborator_WithUnknownClinic_Returns404()
    {
        var client = await Client();

        var response = await PostCollaboratorAsync(client, Guid.NewGuid(), "Luis", "Ramírez");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetCollaborator_AfterCreate_ReturnsIt()
    {
        var client = await Client();
        var clinic = await CreateClinicAsync(client);
        var created = await CreateCollaboratorAsync(client, clinic.Id, "Sofía", "Ríos");

        var response = await client.GetAsync($"{Base}/{created.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var collaborator = await response.Content.ReadFromJsonAsync<CollaboratorResponse>();
        collaborator!.Id.Should().Be(created.Id);
        collaborator.LastName.Should().Be("Ríos");
    }

    [Fact]
    public async Task GetCollaborator_WithUnknownId_Returns404()
    {
        var client = await Client();
        var response = await client.GetAsync($"{Base}/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeactivateCollaborator_Returns204AndIsNoLongerFound()
    {
        var client = await Client();
        var clinic = await CreateClinicAsync(client);
        var created = await CreateCollaboratorAsync(client, clinic.Id, "Temporal", "Test");

        var deleteResponse = await client.DeleteAsync($"{Base}/{created.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await client.GetAsync($"{Base}/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListCollaborators_WithoutToken_Returns401()
    {
        var response = await factory.CreateClient().GetAsync(Base);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListCollaborators_DefaultPaging_ReturnsEnvelopeWithPageOneAndSizeTwenty()
    {
        var client = await Client();
        var clinic = await CreateClinicAsync(client);
        await CreateCollaboratorAsync(client, clinic.Id, "Paginado", "Uno");

        var response = await client.GetAsync(Base);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var paged = await response.Content.ReadFromJsonAsync<PagedResult<CollaboratorResponse>>();
        paged!.Items.Should().NotBeEmpty();
        paged.Page.Should().Be(1);
        paged.PageSize.Should().Be(20);
        paged.TotalCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ListCollaborators_Page2_ReturnsNextSlice()
    {
        var client = await Client();
        var clinic = await CreateClinicAsync(client);
        for (var i = 0; i < 3; i++)
            await CreateCollaboratorAsync(client, clinic.Id, $"Slice{i}", $"Test{Guid.NewGuid():N}"[..10]);

        var page1 = await (await client.GetAsync($"{Base}?page=1&pageSize=1"))
            .Content.ReadFromJsonAsync<PagedResult<CollaboratorResponse>>();
        var page2 = await (await client.GetAsync($"{Base}?page=2&pageSize=1"))
            .Content.ReadFromJsonAsync<PagedResult<CollaboratorResponse>>();

        page1!.Items.Should().HaveCount(1);
        page2!.Items.Should().HaveCount(1);
        page2.Page.Should().Be(2);
        page1.Items[0].Id.Should().NotBe(page2.Items[0].Id);
    }

    [Fact]
    public async Task ListCollaborators_PageSizeAbove100_ClampsTo100()
    {
        var client = await Client();

        var response = await client.GetAsync($"{Base}?page=1&pageSize=500");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var paged = await response.Content.ReadFromJsonAsync<PagedResult<CollaboratorResponse>>();
        paged!.PageSize.Should().Be(100);
    }

    // ─── GET /collaborators/me ────────────────────────────────────────────────

    [Fact]
    public async Task GetMe_WithMatchingEmail_ReturnsCollaborator()
    {
        var admin = await Client();
        var clinic = await CreateClinicAsync(admin);
        var email = $"me-{Guid.NewGuid():N}@test.local";

        await InsertUserAsync(email, "Member123!test");
        var collaborator = await CreateCollaboratorAsync(admin, clinic.Id, "Ana", "Match", email);

        var memberClient = await LoginAsAsync(email, "Member123!test");
        var response = await memberClient.GetAsync($"{Base}/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var me = await response.Content.ReadFromJsonAsync<CollaboratorResponse>();
        me!.Id.Should().Be(collaborator.Id);
    }

    [Fact]
    public async Task GetMe_WithDifferentCasedEmail_ReturnsCollaborator()
    {
        var admin = await Client();
        var clinic = await CreateClinicAsync(admin);
        var uniquePart = Guid.NewGuid().ToString("N");
        var userEmail = $"Me-{uniquePart}@Test.local";
        var collaboratorEmail = $"me-{uniquePart}@test.local"; // same address, different casing

        await InsertUserAsync(userEmail, "Member123!test");
        var collaborator = await CreateCollaboratorAsync(admin, clinic.Id, "Ana", "CaseMatch", collaboratorEmail);

        var memberClient = await LoginAsAsync(userEmail, "Member123!test");
        var response = await memberClient.GetAsync($"{Base}/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var me = await response.Content.ReadFromJsonAsync<CollaboratorResponse>();
        me!.Id.Should().Be(collaborator.Id);
    }

    [Fact]
    public async Task GetMe_WithNoMatchingCollaborator_Returns404()
    {
        var email = $"nomatch-{Guid.NewGuid():N}@test.local";
        await InsertUserAsync(email, "Member123!test");

        var client = await LoginAsAsync(email, "Member123!test");
        var response = await client.GetAsync($"{Base}/me");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─── POST /collaborators — UserId resolution (Phase 2 provisioning) ───────

    [Fact]
    public async Task CreateCollaborator_WithExplicitUserId_LinksAndReturnsIsLinkedTrue()
    {
        var admin = await Client();
        var clinic = await CreateClinicAsync(admin);
        var userEmail = $"link-{Guid.NewGuid():N}@test.local";
        var userId = await InsertUserAsync(userEmail, "Member123!test");

        // Deliberately a DIFFERENT email than the User's — proves the explicit UserId path
        // does not depend on an email match.
        var response = await PostCollaboratorAsync(
            admin, clinic.Id, "Carla", "Vinculada", $"other-{Guid.NewGuid():N}@test.local", userId);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var collaborator = await response.Content.ReadFromJsonAsync<CollaboratorResponse>();
        collaborator!.UserId.Should().Be(userId);
        collaborator.IsLinked.Should().BeTrue();
    }

    [Fact]
    public async Task CreateCollaborator_WithExplicitUserId_AlreadyLinked_Returns409()
    {
        var admin = await Client();
        var clinic = await CreateClinicAsync(admin);
        var userEmail = $"already-{Guid.NewGuid():N}@test.local";
        var userId = await InsertUserAsync(userEmail, "Member123!test");
        await CreateCollaboratorAsync(
            admin, clinic.Id, "Primero", "Vinculado", $"first-{Guid.NewGuid():N}@test.local", userId);

        var response = await PostCollaboratorAsync(
            admin, clinic.Id, "Segundo", "Duplicado", $"second-{Guid.NewGuid():N}@test.local", userId);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateCollaborator_WithExplicitUserId_UserNotMember_Returns400()
    {
        var admin = await Client();
        var clinic = await CreateClinicAsync(admin);
        var userEmail = $"doctor-{Guid.NewGuid():N}@test.local";
        var userId = await InsertUserAsync(userEmail, "Doctor123!test", RoleNames.Doctor);

        var response = await PostCollaboratorAsync(
            admin, clinic.Id, "No", "Miembro", $"nomember-{Guid.NewGuid():N}@test.local", userId);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateCollaborator_NoUserId_EmailMatchesUnlinkedMember_LinksAutomatically()
    {
        var admin = await Client();
        var clinic = await CreateClinicAsync(admin);
        var email = $"automatch-{Guid.NewGuid():N}@test.local";
        var userId = await InsertUserAsync(email, "Member123!test");

        var response = await PostCollaboratorAsync(admin, clinic.Id, "Auto", "Match", email);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var collaborator = await response.Content.ReadFromJsonAsync<CollaboratorResponse>();
        collaborator!.UserId.Should().Be(userId);
        collaborator.IsLinked.Should().BeTrue();
    }

    [Fact]
    public async Task CreateCollaborator_NoUserIdOrMatch_CreatesPendingRowWithNullUserId()
    {
        var admin = await Client();
        var clinic = await CreateClinicAsync(admin);

        var response = await PostCollaboratorAsync(admin, clinic.Id, "Pendiente", "SinVinculo");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var collaborator = await response.Content.ReadFromJsonAsync<CollaboratorResponse>();
        collaborator!.UserId.Should().BeNull();
        collaborator.IsLinked.Should().BeFalse();
    }

    [Fact]
    public async Task CreateCollaborator_DuplicateEmail_Returns409()
    {
        var admin = await Client();
        var clinic = await CreateClinicAsync(admin);
        var email = $"dup-{Guid.NewGuid():N}@test.local";
        await CreateCollaboratorAsync(admin, clinic.Id, "Original", "Colaborador", email);

        var response = await PostCollaboratorAsync(admin, clinic.Id, "Duplicado", "Colaborador", email);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UpdateCollaborator_DuplicateEmail_Returns409()
    {
        var admin = await Client();
        var clinic = await CreateClinicAsync(admin);
        var emailA = $"updatedup-a-{Guid.NewGuid():N}@test.local";
        var emailB = $"updatedup-b-{Guid.NewGuid():N}@test.local";
        await CreateCollaboratorAsync(admin, clinic.Id, "Alpha", "Uno", emailA);
        var second = await CreateCollaboratorAsync(admin, clinic.Id, "Beta", "Dos", emailB);

        var response = await admin.PutAsJsonAsync($"{Base}/{second.Id}", new UpdateCollaboratorRequest(
            second.FirstName, second.LastName, emailA, second.PhoneNumber, second.Address,
            second.Country, second.State, second.City, second.ProfilePicture,
            second.ClinicId, second.WorkRouteId, second.Positions.Select(p => p.Name).ToList()));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static async Task<ClinicResponse> CreateClinicAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/clinics",
            new CreateClinicRequest($"Clínica {Guid.NewGuid():N}"[..25], "Calle Central"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ClinicResponse>())!;
    }

    private static Task<HttpResponseMessage> PostCollaboratorAsync(
        HttpClient client, Guid clinicId, string firstName, string lastName, string? email = null, Guid? userId = null) =>
        client.PostAsJsonAsync(Base, new CreateCollaboratorRequest(
            firstName, lastName, email ?? $"colab-{Guid.NewGuid():N}@test.local", "8888-8888",
            "Dirección colaborador", "Nicaragua", null, "Managua", null,
            clinicId, null, ["Fisioterapeuta"], userId));

    private static async Task<CollaboratorResponse> CreateCollaboratorAsync(
        HttpClient client, Guid clinicId, string firstName, string lastName, string? email = null, Guid? userId = null)
    {
        var response = await PostCollaboratorAsync(client, clinicId, firstName, lastName, email, userId);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CollaboratorResponse>())!;
    }

    /// <summary>Direct DbContext insert (mirrors <c>ReportTests.SeedUserAsync</c>) — a known
    /// password lets the test log in afterward via <see cref="LoginAsAsync"/>, unlike
    /// ReportTests' variant which never needs to authenticate as the seeded user. Returns the
    /// new User's Id so callers can exercise the explicit-UserId resolution path.</summary>
    private async Task<Guid> InsertUserAsync(string email, string password, string roleName = RoleNames.Member)
    {
        using var scope = factory.Services.CreateScope();
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
