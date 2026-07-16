using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using HouseCenter.Api.Common.Pagination;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Constants;
using HouseCenter.Api.Domain.Entities;
using HouseCenter.Api.Features.Auth;
using HouseCenter.Api.Features.Consultations;
using HouseCenter.Api.Features.Patients;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HouseCenter.Api.Tests;

[Collection("Api")]
public sealed class ConsultationFlowTests(ApiFactory factory)
{
    private const string Password = "Test123!pass";

    private Task<HttpClient> OwnerClient() => factory.CreateAuthenticatedClientAsync();

    // ─── Opening ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task OpenConsultation_WithAssignedDoctor_Returns201()
    {
        var owner = await OwnerClient();
        var (patient, doctorId) = await CreatePatientWithDoctorAsync(owner);

        var response = await owner.PostAsJsonAsync("/consultations", new CreateConsultationRequest(
            patient.Id, null, doctorId,
            "Falta de respuesta en extremidad derecha",
            "Llevamos 3 sesiones y no hay mejora en movilidad", null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var consultation = await response.Content.ReadFromJsonAsync<ConsultationResponse>();
        consultation!.Status.Should().Be("Open");
        consultation.AssignedDoctorId.Should().Be(doctorId);
    }

    [Fact]
    public async Task OpenConsultation_WithUnassignedDoctor_Returns400()
    {
        var owner = await OwnerClient();
        var patient = await CreatePatientAsync(owner);
        var unassignedDoctorId = await SeedUserAsync(RoleNames.Doctor);

        var response = await owner.PostAsJsonAsync("/consultations", new CreateConsultationRequest(
            patient.Id, null, unassignedDoctorId, "Caso", "Mensaje inicial", null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task OpenConsultation_AsMemberNotCollaborator_Returns403()
    {
        var owner = await OwnerClient();
        var (patient, doctorId) = await CreatePatientWithDoctorAsync(owner);
        var (memberClient, _) = await LoginUserWithRoleAsync(RoleNames.Member);

        var response = await memberClient.PostAsJsonAsync("/consultations", new CreateConsultationRequest(
            patient.Id, null, doctorId, "Caso ajeno", "No atiendo a este paciente", null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ─── Message thread + automatic transition ────────────────────────────────

    [Fact]
    public async Task PostMessage_ByDoctor_TransitionsToUnderReview()
    {
        var owner = await OwnerClient();
        var (patient, doctorId, doctorClient) = await CreatePatientWithDoctorClientAsync(owner);
        var consultation = await OpenConsultationAsync(owner, patient.Id, doctorId);

        var response = await doctorClient.PostAsJsonAsync(
            $"/consultations/{consultation.Id}/messages",
            new PostMessageRequest("Esto es normal en las primeras 4 semanas. Continúen.", null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var detail = await doctorClient.GetFromJsonAsync<ConsultationDetailResponse>(
            $"/consultations/{consultation.Id}");
        detail!.Consultation.Status.Should().Be("UnderReview");
        detail.Messages.Should().HaveCount(2); // first message + doctor reply
    }

    [Fact]
    public async Task PostMessage_OnResolvedConsultation_Returns409()
    {
        var owner = await OwnerClient();
        var (patient, doctorId, doctorClient) = await CreatePatientWithDoctorClientAsync(owner);
        var consultation = await OpenConsultationAsync(owner, patient.Id, doctorId);
        await doctorClient.PatchAsJsonAsync($"/consultations/{consultation.Id}/status",
            new UpdateConsultationStatusRequest(ConsultationStatus.Resolved));

        var response = await owner.PostAsJsonAsync(
            $"/consultations/{consultation.Id}/messages",
            new PostMessageRequest("¿Sigue abierto?", null));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ─── Resolution ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveConsultation_ByDoctor_SetsResolvedAt()
    {
        var owner = await OwnerClient();
        var (patient, doctorId, doctorClient) = await CreatePatientWithDoctorClientAsync(owner);
        var consultation = await OpenConsultationAsync(owner, patient.Id, doctorId);

        var response = await doctorClient.PatchAsJsonAsync(
            $"/consultations/{consultation.Id}/status",
            new UpdateConsultationStatusRequest(ConsultationStatus.Resolved));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var resolved = await response.Content.ReadFromJsonAsync<ConsultationResponse>();
        resolved!.Status.Should().Be("Resolved");
        resolved.ResolvedAt.Should().NotBeNull();
    }

    // ─── Role-filtered listing ────────────────────────────────────────────────

    [Fact]
    public async Task ListConsultations_AsDoctor_SeesOnlyAssignedOnes()
    {
        var owner = await OwnerClient();
        var patient = await CreatePatientAsync(owner);
        var (doctorAClient, doctorAId) = await LoginUserWithRoleAsync(RoleNames.Doctor);
        var doctorBId = await SeedUserAsync(RoleNames.Doctor);
        await AssignDoctorAsync(owner, patient.Id, doctorAId);
        await AssignDoctorAsync(owner, patient.Id, doctorBId);
        await OpenConsultationAsync(owner, patient.Id, doctorAId);
        await OpenConsultationAsync(owner, patient.Id, doctorBId);

        var response = await doctorAClient.GetAsync("/consultations");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<PagedResult<ConsultationResponse>>();
        page!.Items.Should().NotBeEmpty();
        page.Items.Should().OnlyContain(c => c.AssignedDoctorId == doctorAId);
    }

    [Fact]
    public async Task GetConsultations_AsSponsor_Returns403()
    {
        var (sponsorClient, _) = await LoginUserWithRoleAsync(RoleNames.Sponsor);

        var response = await sponsorClient.GetAsync("/consultations");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
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
            LastName = "Consulta",
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

    private static async Task<PatientResponse> CreatePatientAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/patients", new CreatePatientRequest(
            $"Consulta{Guid.NewGuid():N}"[..20], "Paciente", null,
            new DateTime(2016, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            Gender.Female, "Nicaragua", null, "Managua", "Dirección 456", null,
            AttentionType.Medical, null, null, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PatientResponse>())!;
    }

    private static async Task AssignDoctorAsync(HttpClient ownerClient, Guid patientId, Guid doctorId)
    {
        var response = await ownerClient.PostAsync($"/patients/{patientId}/doctors/{doctorId}", null);
        response.EnsureSuccessStatusCode();
    }

    private async Task<(PatientResponse Patient, Guid DoctorId)> CreatePatientWithDoctorAsync(
        HttpClient ownerClient)
    {
        var patient = await CreatePatientAsync(ownerClient);
        var doctorId = await SeedUserAsync(RoleNames.Doctor);
        await AssignDoctorAsync(ownerClient, patient.Id, doctorId);
        return (patient, doctorId);
    }

    private async Task<(PatientResponse Patient, Guid DoctorId, HttpClient DoctorClient)>
        CreatePatientWithDoctorClientAsync(HttpClient ownerClient)
    {
        var patient = await CreatePatientAsync(ownerClient);
        var (doctorClient, doctorId) = await LoginUserWithRoleAsync(RoleNames.Doctor);
        await AssignDoctorAsync(ownerClient, patient.Id, doctorId);
        return (patient, doctorId, doctorClient);
    }

    private static async Task<ConsultationResponse> OpenConsultationAsync(
        HttpClient client, Guid patientId, Guid doctorId)
    {
        var response = await client.PostAsJsonAsync("/consultations", new CreateConsultationRequest(
            patientId, null, doctorId, "Consulta de seguimiento", "Primer reporte del caso", null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ConsultationResponse>())!;
    }
}
