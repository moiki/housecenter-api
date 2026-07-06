using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using HouseCenter.Api.Common.Pagination;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Constants;
using HouseCenter.Api.Domain.Entities;
using HouseCenter.Api.Features.Patients;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HouseCenter.Api.Tests;

[Collection("Api")]
public sealed class PatientFlowTests(ApiFactory factory)
{
    private Task<HttpClient> Client() => factory.CreateAuthenticatedClientAsync();

    // ─── Patients ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreatePatient_WithValidData_Returns201()
    {
        var client = await Client();

        var response = await client.PostAsJsonAsync("/patients", ValidPatientRequest("Juan", "Pérez"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var patient = await response.Content.ReadFromJsonAsync<PatientResponse>();
        patient!.FirstName.Should().Be("Juan");
        patient.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task CreatePatient_WithEmptyName_Returns400()
    {
        var client = await Client();

        var response = await client.PostAsJsonAsync("/patients",
            ValidPatientRequest("", "Pérez"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetPatient_AfterCreate_ReturnsIt()
    {
        var client = await Client();
        var created = await CreatePatientAsync(client, "María");

        var response = await client.GetAsync($"/patients/{created.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var patient = await response.Content.ReadFromJsonAsync<PatientResponse>();
        patient!.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task GetPatient_UnknownId_Returns404()
    {
        var client = await Client();
        var response = await client.GetAsync($"/patients/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListPatients_ReturnsPaged()
    {
        var client = await Client();
        await CreatePatientAsync(client, "Ana");

        var response = await client.GetAsync("/patients?page=1&pageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var paged = await response.Content.ReadFromJsonAsync<PagedResult<PatientResponse>>();
        paged!.Items.Should().NotBeEmpty();
        paged.Page.Should().Be(1);
        paged.TotalCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task DeactivatePatient_Returns204_ThenGetReturns404()
    {
        var client = await Client();
        var created = await CreatePatientAsync(client, "Temporal");

        var deleteResponse = await client.DeleteAsync($"/patients/{created.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await client.GetAsync($"/patients/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─── Full summary (Task.WhenAll) ──────────────────────────────────────────

    [Fact]
    public async Task GetFullSummary_ReturnsPatientTreatmentsAndComments()
    {
        var client = await Client();
        var patient = await CreatePatientAsync(client, "FullSummaryTest");

        // Add a treatment
        await client.PostAsJsonAsync($"/patients/{patient.Id}/treatments",
            new CreateTreatmentRequest("Tratamiento A", "Desc", null,
                DateTime.UtcNow, DateTime.UtcNow.AddMonths(3)));

        // Add a comment
        await client.PostAsJsonAsync($"/patients/{patient.Id}/comments",
            new CreatePatientCommentRequest("Nota inicial", CommentType.Simple));

        var response = await client.GetAsync($"/patients/{patient.Id}/full-summary");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var summary = await response.Content.ReadFromJsonAsync<PatientFullSummaryResponse>();
        summary!.Patient.Id.Should().Be(patient.Id);
        summary.Treatments.Should().HaveCount(1);
        summary.Comments.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetFullSummary_WithAssignedDoctor_IncludesDoctor()
    {
        var client = await Client();
        var patient = await CreatePatientAsync(client, "DoctorSummaryTest");
        var (doctorId, doctorEmail) = await SeedDoctorAsync();

        var assignResponse = await client.PostAsync($"/patients/{patient.Id}/doctors/{doctorId}", null);
        assignResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await client.GetAsync($"/patients/{patient.Id}/full-summary");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var summary = await response.Content.ReadFromJsonAsync<PatientFullSummaryResponse>();
        summary!.AssignedDoctors.Should().ContainSingle(d => d.Id == doctorId && d.Email == doctorEmail);
    }

    // ─── Treatments ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateTreatment_Returns201_WithCorrectPatientId()
    {
        var client = await Client();
        var patient = await CreatePatientAsync(client, "TreatmentOwner");

        var response = await client.PostAsJsonAsync($"/patients/{patient.Id}/treatments",
            new CreateTreatmentRequest("Fisioterapia", "Recuperación motora", null,
                DateTime.UtcNow, DateTime.UtcNow.AddMonths(2)));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var treatment = await response.Content.ReadFromJsonAsync<TreatmentResponse>();
        treatment!.PatientId.Should().Be(patient.Id);
        treatment.Status.Should().Be("Active");
    }

    [Fact]
    public async Task CreateTreatment_WithOffsetLessDateStrings_Returns201()
    {
        // Regression test: a real client (Postman, a browser Date without toISOString)
        // can send "2024-01-15T00:00:00" with no "Z"/offset. System.Text.Json deserializes
        // that as DateTime.Kind=Unspecified, which Npgsql rejects for "timestamp with time
        // zone" columns unless HouseCenterDbContext's global UTC converter normalizes it.
        var client = await Client();
        var patient = await CreatePatientAsync(client, "OffsetLessDatesPatient");

        var content = new StringContent(
            """{"name":"Fisioterapia","description":"Recuperación motora","profile":null,"startDate":"2024-01-15T00:00:00","endDate":"2024-04-15T00:00:00"}""",
            Encoding.UTF8, "application/json");

        var response = await client.PostAsync($"/patients/{patient.Id}/treatments", content);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task UpdateTreatmentStatus_ToPaused_Succeeds()
    {
        var client = await Client();
        var patient = await CreatePatientAsync(client, "StatusPatient");
        var treatment = await CreateTreatmentAsync(client, patient.Id);

        var response = await client.PatchAsJsonAsync(
            $"/patients/{patient.Id}/treatments/{treatment.Id}/status",
            new UpdateTreatmentStatusRequest(TreatmentStatus.Paused));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<TreatmentResponse>();
        updated!.Status.Should().Be("Paused");
    }

    [Fact]
    public async Task ListTreatmentDetails_AfterCreate_ReturnsIt()
    {
        var client = await Client();
        var patient = await CreatePatientAsync(client, "DetailsPatient");
        var treatment = await CreateTreatmentAsync(client, patient.Id);

        await client.PostAsJsonAsync($"/treatments/{treatment.Id}/details",
            new CreateTreatmentDetailRequest("Sesión 1", "Progreso notable", null, DateTime.UtcNow));

        var response = await client.GetAsync($"/treatments/{treatment.Id}/details");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var details = await response.Content.ReadFromJsonAsync<PagedResult<TreatmentDetailResponse>>();
        details!.Items.Should().ContainSingle(d => d.Name == "Sesión 1");
    }

    [Fact]
    public async Task ListTreatmentDetails_UnknownTreatment_ReturnsNotFound()
    {
        var client = await Client();

        var response = await client.GetAsync($"/treatments/{Guid.NewGuid()}/details");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListTreatmentComments_AfterCreate_ReturnsIt()
    {
        var client = await Client();
        var patient = await CreatePatientAsync(client, "CommentsPatient");
        var treatment = await CreateTreatmentAsync(client, patient.Id);

        await client.PostAsJsonAsync($"/treatments/{treatment.Id}/comments",
            new CreateTreatmentCommentRequest("Buen avance", CommentType.Medical));

        var response = await client.GetAsync($"/treatments/{treatment.Id}/comments");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var comments = await response.Content.ReadFromJsonAsync<PagedResult<TreatmentCommentResponse>>();
        comments!.Items.Should().ContainSingle(c => c.Body == "Buen avance");
    }

    [Fact]
    public async Task ListTreatmentComments_UnknownTreatment_ReturnsNotFound()
    {
        var client = await Client();

        var response = await client.GetAsync($"/treatments/{Guid.NewGuid()}/comments");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static CreatePatientRequest ValidPatientRequest(string firstName, string lastName) =>
        new(firstName, lastName, null, new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Gender.Male, "Nicaragua", null, "Managua", "Dirección 123", null,
            AttentionType.Medical, null, null);

    private static async Task<PatientResponse> CreatePatientAsync(HttpClient client, string firstName)
    {
        var response = await client.PostAsJsonAsync("/patients", ValidPatientRequest(firstName, "Test"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PatientResponse>())!;
    }

    private static async Task<TreatmentResponse> CreateTreatmentAsync(HttpClient client, Guid patientId)
    {
        var response = await client.PostAsJsonAsync($"/patients/{patientId}/treatments",
            new CreateTreatmentRequest("Tratamiento", "Descripción", null,
                DateTime.UtcNow, DateTime.UtcNow.AddMonths(1)));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TreatmentResponse>())!;
    }

    /// <summary>Inserts a Doctor-role user straight into the test DB.</summary>
    private async Task<(Guid Id, string Email)> SeedDoctorAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HouseCenterDbContext>();
        var role = await db.Roles.SingleAsync(r => r.Name == RoleNames.Doctor);

        var doctor = new User
        {
            FirstName = "Doctor",
            LastName = "Test",
            Email = $"doctor-{Guid.NewGuid():N}@test.local",
            HashedPassword = BCrypt.Net.BCrypt.HashPassword("Test123!pass"),
            Address = "Test 123",
            EmailConfirmed = true,
            Roles = [role]
        };

        db.Users.Add(doctor);
        await db.SaveChangesAsync();
        return (doctor.Id, doctor.Email);
    }
}
