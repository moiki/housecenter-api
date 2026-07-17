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
using HouseCenter.Api.Features.Invitations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HouseCenter.Api.Tests;

[Collection("Api")]
public sealed class SignupFlowTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task FullSignupFlow_InvitationToLogin_Succeeds()
    {
        const string inviteeEmail = "new.member@test.local";
        const string inviteePassword = "Member123!test";

        // 1. Owner creates an invitation for a Member
        var ownerTokens = await LoginAsOwnerAsync();
        var memberRoleId = await GetRoleIdAsync(RoleNames.Member);

        var createResponse = await SendAuthorizedAsync(HttpMethod.Post, "/invitations",
            ownerTokens.AccessToken, new CreateInvitationRequest(inviteeEmail, memberRoleId));

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var invitation = (await createResponse.Content.ReadFromJsonAsync<InvitationResponse>())!;

        // 2. Recipient receives the token by email — here we mint it the same way the email does
        var invitationToken = await CreateInvitationTokenAsync(invitation.Id);

        // 3. Recipient validates the invitation before showing the signup form
        var validateResponse = await _client.GetAsync(
            $"/invitations/validate?token={Uri.EscapeDataString(invitationToken)}");

        validateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var validation = (await validateResponse.Content.ReadFromJsonAsync<InvitationValidationResponse>())!;
        validation.Email.Should().Be(inviteeEmail);
        validation.RoleName.Should().Be(RoleNames.Member);

        // 4. Recipient signs up — gets a token pair back (auto-login)
        var signupResponse = await _client.PostAsJsonAsync("/auth/signup", new SignupRequest(
            invitationToken, "New", "Member", inviteePassword,
            "Some address 123", null, null, null, null,
            "signup-flow-device", null, DevicePlatform.Web));

        signupResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var signupTokens = await signupResponse.Content.ReadFromJsonAsync<TokenPairResponse>();
        signupTokens!.AccessToken.Should().NotBeNullOrWhiteSpace();

        // 5. Invitation is consumed
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HouseCenterDbContext>();
            var stored = await db.Invitations.SingleAsync(i => i.Id == invitation.Id);
            stored.Status.Should().Be(InvitationStatus.Accepted);
        }

        // 6. The new member can log in with their own credentials
        var loginResponse = await _client.PostAsJsonAsync("/auth/login",
            new LoginRequest(inviteeEmail, inviteePassword, "signup-flow-device", null, DevicePlatform.Web));
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 7. A consumed invitation cannot be used twice
        var reuseResponse = await _client.PostAsJsonAsync("/auth/signup", new SignupRequest(
            invitationToken, "Evil", "Twin", "Hacker123!", "Nowhere", null, null, null, null,
            "signup-flow-device", null, DevicePlatform.Web));
        reuseResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateInvitation_WithoutAuth_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/invitations",
            new CreateInvitationRequest("anyone@test.local", Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListInvitations_AfterCreate_ReturnsIt()
    {
        var ownerTokens = await LoginAsOwnerAsync();
        var memberRoleId = await GetRoleIdAsync(RoleNames.Member);

        var createResponse = await SendAuthorizedAsync(HttpMethod.Post, "/invitations",
            ownerTokens.AccessToken, new CreateInvitationRequest("list-check@test.local", memberRoleId));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var listRequest = new HttpRequestMessage(HttpMethod.Get, "/invitations");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokens.AccessToken);
        var listResponse = await _client.SendAsync(listRequest);

        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await listResponse.Content.ReadFromJsonAsync<PagedResult<InvitationResponse>>();
        page!.Items.Should().Contain(i => i.Email == "list-check@test.local" && i.Status == "Pending");
    }

    [Fact]
    public async Task ListInvitations_WithoutAuth_Returns401()
    {
        var response = await _client.GetAsync("/invitations");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Signup_WithGarbageToken_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/auth/signup", new SignupRequest(
            "not-a-real-token", "First", "Last", "Password123!", "Address", null, null, null, null,
            "signup-flow-device", null, DevicePlatform.Web));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─── Member signup links an existing Collaborator, never fabricates one (Phase 2) ─────

    [Fact]
    public async Task Signup_WithMatchingUnlinkedCollaborator_LinksExistingRosterRow()
    {
        var inviteeEmail = $"roster-{Guid.NewGuid():N}@test.local";
        const string inviteePassword = "Member123!test";

        // An admin already rostered this person (e.g. from a paper form) before the
        // invitation existed — a pending/legacy Collaborator with no UserId.
        var adminClient = await factory.CreateAuthenticatedClientAsync();
        var clinic = await CreateClinicAsync(adminClient);
        var collaborator = await CreateCollaboratorAsync(adminClient, clinic.Id, "Roster", "Pendiente", inviteeEmail);
        collaborator.UserId.Should().BeNull();
        collaborator.IsLinked.Should().BeFalse();

        var ownerTokens = await LoginAsOwnerAsync();
        var memberRoleId = await GetRoleIdAsync(RoleNames.Member);
        var createResponse = await SendAuthorizedAsync(HttpMethod.Post, "/invitations",
            ownerTokens.AccessToken, new CreateInvitationRequest(inviteeEmail, memberRoleId));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var invitation = (await createResponse.Content.ReadFromJsonAsync<InvitationResponse>())!;
        var invitationToken = await CreateInvitationTokenAsync(invitation.Id);

        var signupResponse = await _client.PostAsJsonAsync("/auth/signup", new SignupRequest(
            invitationToken, "Roster", "Member", inviteePassword,
            "Some address 123", null, null, null, null,
            "signup-flow-device-roster", null, DevicePlatform.Web));
        signupResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var signupTokens = (await signupResponse.Content.ReadFromJsonAsync<TokenPairResponse>())!;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HouseCenterDbContext>();
            var newUser = await db.Users.SingleAsync(u => u.Email == inviteeEmail);

            var rosterRows = await db.Collaborators
                .Where(c => c.Email == inviteeEmail.ToLowerInvariant())
                .ToListAsync();
            rosterRows.Should().ContainSingle(); // linked, never a second/fabricated row
            rosterRows[0].Id.Should().Be(collaborator.Id);
            rosterRows[0].UserId.Should().Be(newUser.Id);
        }

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signupTokens.AccessToken);
        var meResponse = await _client.GetAsync("/collaborators/me");

        meResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var me = await meResponse.Content.ReadFromJsonAsync<CollaboratorResponse>();
        me!.Id.Should().Be(collaborator.Id);
    }

    [Fact]
    public async Task Signup_WithNoMatchingCollaborator_LeavesUserUnlinkedWithoutFabricatingRow()
    {
        var inviteeEmail = $"unlinked-{Guid.NewGuid():N}@test.local";
        const string inviteePassword = "Member123!test";

        var ownerTokens = await LoginAsOwnerAsync();
        var memberRoleId = await GetRoleIdAsync(RoleNames.Member);
        var createResponse = await SendAuthorizedAsync(HttpMethod.Post, "/invitations",
            ownerTokens.AccessToken, new CreateInvitationRequest(inviteeEmail, memberRoleId));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var invitation = (await createResponse.Content.ReadFromJsonAsync<InvitationResponse>())!;
        var invitationToken = await CreateInvitationTokenAsync(invitation.Id);

        var signupResponse = await _client.PostAsJsonAsync("/auth/signup", new SignupRequest(
            invitationToken, "Sin", "Roster", inviteePassword,
            "Some address 123", null, null, null, null,
            "signup-flow-device-unlinked", null, DevicePlatform.Web));
        signupResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var signupTokens = (await signupResponse.Content.ReadFromJsonAsync<TokenPairResponse>())!;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HouseCenterDbContext>();
            var rosterRows = await db.Collaborators
                .Where(c => c.Email == inviteeEmail.ToLowerInvariant())
                .ToListAsync();
            rosterRows.Should().BeEmpty(); // never fabricate — Collaborator.ClinicId is required and unknown here
        }

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signupTokens.AccessToken);
        var meResponse = await _client.GetAsync("/collaborators/me");

        meResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<ClinicResponse> CreateClinicAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/clinics",
            new CreateClinicRequest($"Clínica {Guid.NewGuid():N}"[..25], "Calle Central"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ClinicResponse>())!;
    }

    private static async Task<CollaboratorResponse> CreateCollaboratorAsync(
        HttpClient client, Guid clinicId, string firstName, string lastName, string email)
    {
        var response = await client.PostAsJsonAsync("/collaborators", new CreateCollaboratorRequest(
            firstName, lastName, email, "8888-8888", "Dirección colaborador", "Nicaragua", null, "Managua", null,
            clinicId, null, ["Fisioterapeuta"]));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CollaboratorResponse>())!;
    }

    private async Task<TokenPairResponse> LoginAsOwnerAsync()
    {
        var response = await _client.PostAsJsonAsync("/auth/login",
            new LoginRequest(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword, "signup-flow-owner-device", null, DevicePlatform.Web));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenPairResponse>())!;
    }

    private async Task<Guid> GetRoleIdAsync(string roleName)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HouseCenterDbContext>();
        return (await db.Roles.SingleAsync(r => r.Name == roleName)).Id;
    }

    private async Task<string> CreateInvitationTokenAsync(Guid invitationId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HouseCenterDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var invitation = await db.Invitations.SingleAsync(i => i.Id == invitationId);
        return tokenService.CreateInvitationToken(invitation);
    }

    private async Task<HttpResponseMessage> SendAuthorizedAsync<T>(
        HttpMethod method, string url, string accessToken, T body)
    {
        var request = new HttpRequestMessage(method, url)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _client.SendAsync(request);
    }
}
