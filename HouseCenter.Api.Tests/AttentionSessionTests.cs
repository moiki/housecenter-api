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
using HouseCenter.Api.Features.Patients;
using HouseCenter.Api.Features.Patients.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HouseCenter.Api.Tests;

[Collection("Api")]
public sealed class AttentionSessionTests(ApiFactory factory)
{
    private const string Password = "Test123!pass";

    private Task<HttpClient> OwnerClient() => factory.CreateAuthenticatedClientAsync();

    // ─── Session creation ─────────────────────────────────────────────────────

    [Fact]
    public async Task CreateSession_WithClinic_Returns201()
    {
        var client = await OwnerClient();
        var patient = await CreatePatientAsync(client);
        var clinic = await CreateClinicAsync(client);
        var memberId = await SeedUserAsync(RoleNames.Member);

        var response = await client.PostAsJsonAsync($"/patients/{patient.Id}/sessions",
            new CreateAttentionSessionRequest(
                memberId, clinic.Id, null, AttentionType.Medical, DateTime.UtcNow, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var session = await response.Content.ReadFromJsonAsync<AttentionSessionResponse>();
        session!.Status.Should().Be("Scheduled");
        session.ClinicId.Should().Be(clinic.Id);
        session.AttentionType.Should().Be("Medical");
    }

    [Fact]
    public async Task CreateSession_WithoutClinicOrRoute_Returns400()
    {
        var client = await OwnerClient();
        var patient = await CreatePatientAsync(client);
        var memberId = await SeedUserAsync(RoleNames.Member);

        var response = await client.PostAsJsonAsync($"/patients/{patient.Id}/sessions",
            new CreateAttentionSessionRequest(
                memberId, null, null, AttentionType.Medical, DateTime.UtcNow, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ─── Status machine ───────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateStatus_ToCompleted_UpdatesDuration()
    {
        var client = await OwnerClient();
        var (patient, session) = await CreatePatientWithSessionAsync(client);

        var response = await client.PatchAsJsonAsync(
            $"/patients/{patient.Id}/sessions/{session.Id}/status",
            new UpdateSessionStatusRequest(SessionStatus.Completed, 45, "Buena sesión"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<AttentionSessionResponse>();
        updated!.Status.Should().Be("Completed");
        updated.DurationMinutes.Should().Be(45);
        updated.Notes.Should().Be("Buena sesión");
    }

    [Fact]
    public async Task UpdateStatus_ToMissed_NullDuration()
    {
        var client = await OwnerClient();
        var (patient, session) = await CreatePatientWithSessionAsync(client);

        var response = await client.PatchAsJsonAsync(
            $"/patients/{patient.Id}/sessions/{session.Id}/status",
            new UpdateSessionStatusRequest(SessionStatus.Missed, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<AttentionSessionResponse>();
        updated!.Status.Should().Be("Missed");
        updated.DurationMinutes.Should().BeNull();
    }

    [Fact]
    public async Task UpdateStatus_OnCompletedSession_Returns409()
    {
        var client = await OwnerClient();
        var (patient, session) = await CreatePatientWithSessionAsync(client);
        await client.PatchAsJsonAsync(
            $"/patients/{patient.Id}/sessions/{session.Id}/status",
            new UpdateSessionStatusRequest(SessionStatus.Completed, 30, null));

        var response = await client.PatchAsJsonAsync(
            $"/patients/{patient.Id}/sessions/{session.Id}/status",
            new UpdateSessionStatusRequest(SessionStatus.Missed, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ─── Listing and filters ──────────────────────────────────────────────────

    [Fact]
    public async Task ListSessions_FilterByType_ReturnsCorrectSubset()
    {
        var client = await OwnerClient();
        var patient = await CreatePatientAsync(client);
        var clinic = await CreateClinicAsync(client);
        var memberId = await SeedUserAsync(RoleNames.Member);
        await CreateSessionAsync(client, patient.Id, memberId, clinic.Id, AttentionType.Medical);
        await CreateSessionAsync(client, patient.Id, memberId, clinic.Id, AttentionType.EducationalReinforcement);

        var response = await client.GetAsync($"/patients/{patient.Id}/sessions?type=Medical");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<PagedResult<AttentionSessionResponse>>();
        page!.Items.Should().HaveCount(1);
        page.Items[0].AttentionType.Should().Be("Medical");
    }

    [Fact]
    public async Task GetSessions_AsSponsor_Returns403()
    {
        var ownerClient = await OwnerClient();
        var patient = await CreatePatientAsync(ownerClient);
        var sponsorClient = await LoginUserWithRoleAsync(RoleNames.Sponsor);

        var response = await sponsorClient.GetAsync($"/patients/{patient.Id}/sessions");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ─── Doctor assignment ────────────────────────────────────────────────────

    [Fact]
    public async Task AddDoctor_ValidDoctorRole_Returns204()
    {
        var client = await OwnerClient();
        var patient = await CreatePatientAsync(client);
        var doctorId = await SeedUserAsync(RoleNames.Doctor);

        var response = await client.PostAsync($"/patients/{patient.Id}/doctors/{doctorId}", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task AddDoctor_WithMemberRole_Returns400()
    {
        var client = await OwnerClient();
        var patient = await CreatePatientAsync(client);
        var memberId = await SeedUserAsync(RoleNames.Member);

        var response = await client.PostAsync($"/patients/{patient.Id}/doctors/{memberId}", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RemoveDoctor_AfterAssign_Returns204()
    {
        var client = await OwnerClient();
        var patient = await CreatePatientAsync(client);
        var doctorId = await SeedUserAsync(RoleNames.Doctor);
        await client.PostAsync($"/patients/{patient.Id}/doctors/{doctorId}", null);

        var response = await client.DeleteAsync($"/patients/{patient.Id}/doctors/{doctorId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Inserts a user with the given role straight into the test DB.</summary>
    private async Task<Guid> SeedUserAsync(string roleName, string? email = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HouseCenterDbContext>();
        var role = await db.Roles.SingleAsync(r => r.Name == roleName);

        var user = new User
        {
            FirstName = roleName,
            LastName = "Test",
            Email = email ?? $"{roleName.ToLower()}-{Guid.NewGuid():N}@test.local",
            HashedPassword = BCrypt.Net.BCrypt.HashPassword(Password),
            Address = "Test 123",
            EmailConfirmed = true,
            Roles = [role]
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<HttpClient> LoginUserWithRoleAsync(string roleName)
    {
        var email = $"{roleName.ToLower()}-{Guid.NewGuid():N}@test.local";
        await SeedUserAsync(roleName, email);

        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/auth/login",
            new LoginRequest(email, Password, "test-device", null, DevicePlatform.Web));
        response.EnsureSuccessStatusCode();

        var tokens = await response.Content.ReadFromJsonAsync<TokenPairResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return client;
    }

    private static async Task<PatientResponse> CreatePatientAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/patients", new CreatePatientRequest(
            $"Paciente{Guid.NewGuid():N}"[..20], "Sesiones", null,
            new DateTime(2015, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Gender.Male, "Nicaragua", null, "Managua", "Dirección 123", null,
            AttentionType.Medical, null, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PatientResponse>())!;
    }

    private static async Task<ClinicResponse> CreateClinicAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/clinics",
            new CreateClinicRequest($"Clínica {Guid.NewGuid():N}"[..25], "Calle Central"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ClinicResponse>())!;
    }

    private static async Task<AttentionSessionResponse> CreateSessionAsync(
        HttpClient client, Guid patientId, Guid collaboratorId, Guid clinicId,
        AttentionType type = AttentionType.Medical)
    {
        var response = await client.PostAsJsonAsync($"/patients/{patientId}/sessions",
            new CreateAttentionSessionRequest(
                collaboratorId, clinicId, null, type, DateTime.UtcNow, null, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AttentionSessionResponse>())!;
    }

    private async Task<(PatientResponse Patient, AttentionSessionResponse Session)>
        CreatePatientWithSessionAsync(HttpClient client)
    {
        var patient = await CreatePatientAsync(client);
        var clinic = await CreateClinicAsync(client);
        var memberId = await SeedUserAsync(RoleNames.Member);
        var session = await CreateSessionAsync(client, patient.Id, memberId, clinic.Id);
        return (patient, session);
    }
}
