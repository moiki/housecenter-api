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
using HouseCenter.Api.Features.Patients;
using HouseCenter.Api.Features.Patients.Sessions;
using HouseCenter.Api.Features.Reports;
using HouseCenter.Api.Features.WorkRoutes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HouseCenter.Api.Tests;

[Collection("Api")]
public sealed class ReportTests(ApiFactory factory)
{
    private const string Password = "Test123!pass";

    private Task<HttpClient> OwnerClient() => factory.CreateAuthenticatedClientAsync();

    // ─── Summary ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSummary_ReturnsCorrectCounts()
    {
        var owner = await OwnerClient();
        var clinic = await CreateClinicAsync(owner);
        var patient = await CreatePatientAsync(owner);
        var memberId = await SeedUserAsync(RoleNames.Member);
        await CreateSessionAsync(owner, patient.Id, memberId, clinicId: clinic.Id);

        var response = await owner.GetAsync("/reports/summary");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var summary = await response.Content.ReadFromJsonAsync<SummaryReportResponse>();
        summary!.ActivePatients.Should().BeGreaterThanOrEqualTo(1);
        summary.TotalPatients.Should().BeGreaterThanOrEqualTo(summary.ActivePatients);
        summary.Clinics.Should().BeGreaterThanOrEqualTo(1);
        summary.SessionsThisMonth.Should().BeGreaterThanOrEqualTo(1);
        summary.SessionsByAttentionType.Should().ContainKey("Medical")
            .WhoseValue.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GetSummary_AsSponsor_Returns200()
    {
        var (sponsorClient, _) = await LoginUserWithRoleAsync(RoleNames.Sponsor);

        var response = await sponsorClient.GetAsync("/reports/summary");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var summary = await response.Content.ReadFromJsonAsync<SummaryReportResponse>();
        summary.Should().NotBeNull();
    }

    // ─── Clinic report ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetClinicReport_AdminSeesCollaboratorNames()
    {
        var owner = await OwnerClient();
        var clinic = await CreateClinicAsync(owner);
        await CreateCollaboratorAsync(owner, clinic.Id, "Carlos", "Reyes");

        var response = await owner.GetAsync($"/reports/clinics/{clinic.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var report = await response.Content.ReadFromJsonAsync<ClinicReportResponse>();
        report!.CollaboratorsAssigned.Should().Be(1);
        report.CollaboratorNames.Should().NotBeNull().And.Contain("Carlos Reyes");
    }

    [Fact]
    public async Task GetClinicReport_AsSponsor_NamesAreNull()
    {
        var owner = await OwnerClient();
        var clinic = await CreateClinicAsync(owner);
        await CreateCollaboratorAsync(owner, clinic.Id, "Ana", "Privada");
        var (sponsorClient, _) = await LoginUserWithRoleAsync(RoleNames.Sponsor);

        var response = await sponsorClient.GetAsync($"/reports/clinics/{clinic.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var report = await response.Content.ReadFromJsonAsync<ClinicReportResponse>();
        report!.CollaboratorsAssigned.Should().Be(1); // counts stay visible
        report.CollaboratorNames.Should().BeNull();   // names do not
    }

    [Fact]
    public async Task GetClinicReport_UnknownId_Returns404()
    {
        var owner = await OwnerClient();
        var response = await owner.GetAsync($"/reports/clinics/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─── Work route report ────────────────────────────────────────────────────

    [Fact]
    public async Task GetWorkRouteReport_ReturnsCounts()
    {
        var owner = await OwnerClient();
        var clinic = await CreateClinicAsync(owner);
        var route = await CreateWorkRouteAsync(owner, clinic.Id);
        // Stops are derived from patients assigned to the route, not a separate list.
        await CreatePatientAsync(owner, workRouteId: route.Id);
        await CreatePatientAsync(owner, workRouteId: route.Id);
        var patient = await CreatePatientAsync(owner);
        var memberId = await SeedUserAsync(RoleNames.Member);
        await CreateSessionAsync(owner, patient.Id, memberId, workRouteId: route.Id);

        var response = await owner.GetAsync($"/reports/work-routes/{route.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var report = await response.Content.ReadFromJsonAsync<WorkRouteReportResponse>();
        report!.StopsCount.Should().Be(2);
        report.SessionsLast30Days.Should().BeGreaterThanOrEqualTo(1);
        report.PatientsVisited.Should().BeGreaterThanOrEqualTo(1);
        report.CollaboratorNames.Should().NotBeNull(); // owner is management
    }

    [Fact]
    public async Task GetWorkRouteReport_AsDoctor_Returns403()
    {
        var owner = await OwnerClient();
        var clinic = await CreateClinicAsync(owner);
        var route = await CreateWorkRouteAsync(owner, clinic.Id);
        var (doctorClient, _) = await LoginUserWithRoleAsync(RoleNames.Doctor);

        var response = await doctorClient.GetAsync($"/reports/work-routes/{route.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ─── Session period report ────────────────────────────────────────────────

    [Fact]
    public async Task GetSessionPeriod_EmptyRange_ReturnsZeroCounts()
    {
        var owner = await OwnerClient();

        var response = await owner.GetAsync("/reports/sessions?from=2020-01-01&to=2020-01-31");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var report = await response.Content.ReadFromJsonAsync<SessionPeriodReportResponse>();
        report!.Weeks.Should().BeEmpty();
        report.SessionsByCollaborator.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task GetSessionPeriod_InvalidRange_Returns400()
    {
        var owner = await OwnerClient();
        var response = await owner.GetAsync("/reports/sessions?from=2026-02-01&to=2026-01-01");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetSessionPeriod_AsMember_SeesOnlyOwnSessions()
    {
        var owner = await OwnerClient();
        var clinic = await CreateClinicAsync(owner);
        var patient = await CreatePatientAsync(owner);
        var (memberClient, memberAId) = await LoginUserWithRoleAsync(RoleNames.Member);
        var memberBId = await SeedUserAsync(RoleNames.Member);
        await CreateSessionAsync(owner, patient.Id, memberAId, clinicId: clinic.Id);
        await CreateSessionAsync(owner, patient.Id, memberBId, clinicId: clinic.Id);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var response = await memberClient.GetAsync(
            $"/reports/sessions?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var report = await response.Content.ReadFromJsonAsync<SessionPeriodReportResponse>();
        report!.Weeks.Sum(w => w.Count).Should().Be(1);      // memberB's session filtered out
        report.SessionsByCollaborator.Should().BeNull();      // breakdown is management-only
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private async Task<Guid> SeedUserAsync(string roleName, string? email = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HouseCenterDbContext>();
        var role = await db.Roles.SingleAsync(r => r.Name == roleName);

        var user = new User
        {
            FirstName = roleName,
            LastName = "Reporte",
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

    private async Task<(HttpClient Client, Guid UserId)> LoginUserWithRoleAsync(string roleName)
    {
        var email = $"{roleName.ToLower()}-{Guid.NewGuid():N}@test.local";
        var userId = await SeedUserAsync(roleName, email);

        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/auth/login",
            new LoginRequest(email, Password, "test-device", null, DevicePlatform.Web));
        response.EnsureSuccessStatusCode();

        var tokens = await response.Content.ReadFromJsonAsync<TokenPairResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return (client, userId);
    }

    private static async Task<PatientResponse> CreatePatientAsync(HttpClient client, Guid? workRouteId = null)
    {
        var response = await client.PostAsJsonAsync("/patients", new CreatePatientRequest(
            $"Reporte{Guid.NewGuid():N}"[..20], "Paciente", null,
            new DateTime(2017, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            Gender.Male, "Nicaragua", null, "Managua", "Dirección 789", null,
            AttentionType.Medical, null, workRouteId, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PatientResponse>())!;
    }

    private static async Task<ClinicResponse> CreateClinicAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/clinics",
            new CreateClinicRequest($"Reporte {Guid.NewGuid():N}"[..25], "Calle Central"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ClinicResponse>())!;
    }

    private static async Task<WorkRouteResponse> CreateWorkRouteAsync(HttpClient client, Guid clinicId)
    {
        var response = await client.PostAsJsonAsync("/workroutes", new CreateWorkRouteRequest(
            $"Ruta {Guid.NewGuid():N}"[..20], "Ruta de prueba", null, clinicId,
            ["Tuesday", "Thursday"],
            DateOnly.FromDateTime(DateTime.UtcNow),
            null,
            true));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkRouteResponse>())!;
    }

    private static async Task CreateCollaboratorAsync(
        HttpClient client, Guid clinicId, string firstName, string lastName)
    {
        var response = await client.PostAsJsonAsync("/collaborators", new CreateCollaboratorRequest(
            firstName, lastName, $"colab-{Guid.NewGuid():N}@test.local", "8888-8888",
            "Dirección colaborador", "Nicaragua", null, "Managua", null,
            clinicId, null, ["Fisioterapeuta"]));
        response.EnsureSuccessStatusCode();
    }

    private static async Task CreateSessionAsync(
        HttpClient client, Guid patientId, Guid collaboratorId,
        Guid? clinicId = null, Guid? workRouteId = null)
    {
        var response = await client.PostAsJsonAsync($"/patients/{patientId}/sessions",
            new CreateAttentionSessionRequest(
                collaboratorId, clinicId, workRouteId,
                AttentionType.Medical, DateTime.UtcNow, null, null));
        response.EnsureSuccessStatusCode();
    }
}
