using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Constants;
using HouseCenter.Api.Domain.Entities;
using HouseCenter.Api.Features.Auth;
using HouseCenter.Api.Features.Patients;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HouseCenter.Api.Tests;

/// <summary>
/// RBAC coverage for openspec/changes/patients-rbac-restriction: Sponsor is blocked from every
/// route in this file's scope (RNF-029); Doctor may read but not write; Member may create/update/
/// assign a collaborator but not deactivate; Administrator and Owner pass every tier. The two
/// doctor-assignment routes stay Owner-only, unaffected by this change (spec Decision 4).
/// </summary>
[Collection("Api")]
public sealed class PatientRbacTests(ApiFactory factory)
{
    private const string Password = "Test123!pass";

    private Task<HttpClient> OwnerClient() => factory.CreateAuthenticatedClientAsync();

    /// <summary>IDs of one fully-populated patient record — used to build read/write/deactivate
    /// route lists against a real resource graph.</summary>
    private sealed record PatientGraph(
        Guid PatientId, Guid TreatmentId, Guid DetailId, Guid TreatmentCommentId, Guid PatientCommentId);

    // ─── Read tier: StaffOnly (Phase 1) ────────────────────────────────────────

    [Fact]
    public async Task GetRoutes_AsSponsor_Returns403()
    {
        var owner = await OwnerClient();
        var graph = await RunWriteFlowAsync(owner);
        var (sponsor, _) = await LoginUserWithRoleAsync(RoleNames.Sponsor);

        foreach (var (method, path) in ReadRoutes(graph))
        {
            var response = await sponsor.SendAsync(new HttpRequestMessage(method, path));
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{method} {path} must block Sponsor");
        }
    }

    [Fact]
    public async Task GetRoutes_AsStaffRoles_Returns200()
    {
        var owner = await OwnerClient();
        var graph = await RunWriteFlowAsync(owner);

        var clients = new List<HttpClient> { owner };
        foreach (var role in new[] { RoleNames.Administrator, RoleNames.Member, RoleNames.Doctor })
        {
            var (client, _) = await LoginUserWithRoleAsync(role);
            clients.Add(client);
        }

        foreach (var client in clients)
        foreach (var (method, path) in ReadRoutes(graph))
        {
            var response = await client.SendAsync(new HttpRequestMessage(method, path));
            response.StatusCode.Should().Be(HttpStatusCode.OK, $"{method} {path} must stay allowed for staff roles");
        }
    }

    // ─── Write tier: MemberOrAbove (Phase 2) ───────────────────────────────────

    [Fact]
    public async Task WriteRoutes_AsSponsorOrDoctor_Returns403()
    {
        var owner = await OwnerClient();
        var graph = await RunWriteFlowAsync(owner);
        var collaboratorUserId = await SeedUserAsync(RoleNames.Member);
        var (sponsor, _) = await LoginUserWithRoleAsync(RoleNames.Sponsor);
        var (doctor, _) = await LoginUserWithRoleAsync(RoleNames.Doctor);

        foreach (var client in new[] { sponsor, doctor })
        foreach (var (method, path, body) in WriteRoutes(graph, collaboratorUserId))
        {
            var response = await SendAsync(client, method, path, body);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{method} {path} must block writes for this role");
        }
    }

    [Fact]
    public async Task WriteRoutes_AsMemberAdministratorOrOwner_Succeed()
    {
        var owner = await OwnerClient();
        var (member, _) = await LoginUserWithRoleAsync(RoleNames.Member);
        var (admin, _) = await LoginUserWithRoleAsync(RoleNames.Administrator);

        // RunWriteFlowAsync itself asserts every one of the 12 write-tier routes succeeds —
        // running it once per role IS the assertion for this test.
        foreach (var client in new[] { member, admin, owner })
            await RunWriteFlowAsync(client);
    }

    // ─── Deactivate tier: AdministratorOrAbove (Phase 3) ───────────────────────

    [Fact]
    public async Task DeactivateRoutes_AsMember_Returns403()
    {
        var (member, _) = await LoginUserWithRoleAsync(RoleNames.Member);
        var graph = await RunWriteFlowAsync(member); // Member can create its own graph (Phase 2)

        foreach (var (method, path) in DeactivateRoutes(graph))
        {
            var response = await member.SendAsync(new HttpRequestMessage(method, path));
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{method} {path} must block Member");
        }
    }

    [Fact]
    public async Task DeactivateRoutes_AsAdministratorOrOwner_Returns204()
    {
        var owner = await OwnerClient();
        var (admin, _) = await LoginUserWithRoleAsync(RoleNames.Administrator);

        foreach (var client in new[] { admin, owner })
        {
            var graph = await RunWriteFlowAsync(client);
            foreach (var (method, path) in DeactivateRoutes(graph))
            {
                var response = await client.SendAsync(new HttpRequestMessage(method, path));
                response.StatusCode.Should().Be(HttpStatusCode.NoContent, $"{method} {path} must allow this role");
            }
        }
    }

    // ─── Doctor assignment: OwnerOnly, unchanged (spec Decision 4 regression) ──

    [Fact]
    public async Task DoctorAssignmentRoutes_AsOwner_Succeed()
    {
        var owner = await OwnerClient();
        var graph = await RunWriteFlowAsync(owner);
        var doctorId = await SeedUserAsync(RoleNames.Doctor);

        var assign = await owner.PostAsync($"/patients/{graph.PatientId}/doctors/{doctorId}", null);
        assign.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var remove = await owner.DeleteAsync($"/patients/{graph.PatientId}/doctors/{doctorId}");
        remove.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DoctorAssignmentRoutes_AsAdministrator_Returns403()
    {
        var owner = await OwnerClient();
        var graph = await RunWriteFlowAsync(owner);
        var doctorId = await SeedUserAsync(RoleNames.Doctor);
        await owner.PostAsync($"/patients/{graph.PatientId}/doctors/{doctorId}", null); // pre-assign as owner

        var (admin, _) = await LoginUserWithRoleAsync(RoleNames.Administrator);

        var assign = await admin.PostAsync($"/patients/{graph.PatientId}/doctors/{Guid.NewGuid()}", null);
        assign.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var remove = await admin.DeleteAsync($"/patients/{graph.PatientId}/doctors/{doctorId}");
        remove.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ─── Route inventories ──────────────────────────────────────────────────────

    private static IEnumerable<(HttpMethod Method, string Path)> ReadRoutes(PatientGraph g) =>
    [
        (HttpMethod.Get, "/patients"),
        (HttpMethod.Get, $"/patients/{g.PatientId}"),
        (HttpMethod.Get, $"/patients/{g.PatientId}/full-summary"),
        (HttpMethod.Get, $"/patients/{g.PatientId}/treatments"),
        (HttpMethod.Get, $"/treatments/{g.TreatmentId}/details"),
        (HttpMethod.Get, $"/treatments/{g.TreatmentId}/comments")
    ];

    /// <summary>The 12 create/update/collaborator-assign routes — blocked-role test reuses one
    /// pre-seeded graph since every call here is expected to be rejected before it touches data.</summary>
    private static IEnumerable<(HttpMethod Method, string Path, object? Body)> WriteRoutes(
        PatientGraph g, Guid collaboratorUserId) =>
    [
        (HttpMethod.Post, "/patients", ValidPatientRequest("WriteBlocked")),
        (HttpMethod.Put, $"/patients/{g.PatientId}", ValidUpdatePatientRequest("WriteBlocked")),
        (HttpMethod.Post, $"/patients/{g.PatientId}/collaborators/{collaboratorUserId}", null),
        (HttpMethod.Post, $"/patients/{g.PatientId}/treatments",
            new CreateTreatmentRequest("Bloqueado", "Descripción", null, DateTime.UtcNow, DateTime.UtcNow.AddMonths(1))),
        (HttpMethod.Put, $"/patients/{g.PatientId}/treatments/{g.TreatmentId}",
            new UpdateTreatmentRequest("Bloqueado", "Descripción", null, DateTime.UtcNow, DateTime.UtcNow.AddMonths(1))),
        (HttpMethod.Patch, $"/patients/{g.PatientId}/treatments/{g.TreatmentId}/status",
            new UpdateTreatmentStatusRequest(TreatmentStatus.Paused)),
        (HttpMethod.Post, $"/treatments/{g.TreatmentId}/details",
            new CreateTreatmentDetailRequest("Bloqueado", "Descripción", null, DateTime.UtcNow)),
        (HttpMethod.Put, $"/treatments/{g.TreatmentId}/details/{g.DetailId}",
            new UpdateTreatmentDetailRequest("Bloqueado", "Descripción", null, DateTime.UtcNow)),
        (HttpMethod.Post, $"/treatments/{g.TreatmentId}/comments",
            new CreateTreatmentCommentRequest("Bloqueado", CommentType.Medical)),
        (HttpMethod.Put, $"/treatments/{g.TreatmentId}/comments/{g.TreatmentCommentId}",
            new UpdateTreatmentCommentRequest("Bloqueado", CommentType.Medical, CommentStatus.Accepted)),
        (HttpMethod.Post, $"/patients/{g.PatientId}/comments",
            new CreatePatientCommentRequest("Bloqueado", CommentType.Simple)),
        (HttpMethod.Put, $"/patients/{g.PatientId}/comments/{g.PatientCommentId}",
            new UpdatePatientCommentRequest("Bloqueado", CommentType.Simple, CommentStatus.Accepted))
    ];

    /// <summary>The 5 deactivate (soft-delete) routes. Each entity's IsActive flag is independent
    /// (HouseCenterDbContext's global query filter is per-entity, not cascaded from Patient), so
    /// order across these 5 doesn't matter — deactivating the patient first doesn't hide its
    /// still-active child records from their own by-id lookups.</summary>
    private static IEnumerable<(HttpMethod Method, string Path)> DeactivateRoutes(PatientGraph g) =>
    [
        (HttpMethod.Delete, $"/patients/{g.PatientId}"),
        (HttpMethod.Delete, $"/patients/{g.PatientId}/treatments/{g.TreatmentId}"),
        (HttpMethod.Delete, $"/treatments/{g.TreatmentId}/details/{g.DetailId}"),
        (HttpMethod.Delete, $"/treatments/{g.TreatmentId}/comments/{g.TreatmentCommentId}"),
        (HttpMethod.Delete, $"/patients/{g.PatientId}/comments/{g.PatientCommentId}")
    ];

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string path, object? body)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    /// <summary>Runs the full create+update sequence across all 5 patient-domain resource types
    /// as <paramref name="client"/>, asserting every step succeeds (201/200/204) — this both seeds
    /// a fully-populated <see cref="PatientGraph"/> and proves the write tier for whichever role
    /// <paramref name="client"/> is authenticated as.</summary>
    private async Task<PatientGraph> RunWriteFlowAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var createPatient = await client.PostAsJsonAsync("/patients", ValidPatientRequest($"Rbac{suffix}"));
        createPatient.StatusCode.Should().Be(HttpStatusCode.Created, "create patient must succeed for this role");
        var patient = (await createPatient.Content.ReadFromJsonAsync<PatientResponse>())!;

        var updatePatient = await client.PutAsJsonAsync(
            $"/patients/{patient.Id}", ValidUpdatePatientRequest($"Rbac{suffix}Upd"));
        updatePatient.StatusCode.Should().Be(HttpStatusCode.OK, "update patient must succeed for this role");

        var collaboratorUserId = await SeedUserAsync(RoleNames.Member);
        var addCollaborator = await client.PostAsync(
            $"/patients/{patient.Id}/collaborators/{collaboratorUserId}", null);
        addCollaborator.StatusCode.Should().Be(HttpStatusCode.NoContent, "assign collaborator must succeed for this role");

        var createTreatment = await client.PostAsJsonAsync($"/patients/{patient.Id}/treatments",
            new CreateTreatmentRequest("Tratamiento RBAC", "Descripción", null,
                DateTime.UtcNow, DateTime.UtcNow.AddMonths(1)));
        createTreatment.StatusCode.Should().Be(HttpStatusCode.Created, "create treatment must succeed for this role");
        var treatment = (await createTreatment.Content.ReadFromJsonAsync<TreatmentResponse>())!;

        var updateTreatment = await client.PutAsJsonAsync($"/patients/{patient.Id}/treatments/{treatment.Id}",
            new UpdateTreatmentRequest("Tratamiento RBAC editado", "Descripción editada", null,
                DateTime.UtcNow, DateTime.UtcNow.AddMonths(2)));
        updateTreatment.StatusCode.Should().Be(HttpStatusCode.OK, "update treatment must succeed for this role");

        var updateStatus = await client.PatchAsJsonAsync(
            $"/patients/{patient.Id}/treatments/{treatment.Id}/status",
            new UpdateTreatmentStatusRequest(TreatmentStatus.Paused));
        updateStatus.StatusCode.Should().Be(HttpStatusCode.OK, "update treatment status must succeed for this role");

        var createDetail = await client.PostAsJsonAsync($"/treatments/{treatment.Id}/details",
            new CreateTreatmentDetailRequest("Sesión RBAC", "Progreso", null, DateTime.UtcNow));
        createDetail.StatusCode.Should().Be(HttpStatusCode.Created, "create treatment detail must succeed for this role");
        var detail = (await createDetail.Content.ReadFromJsonAsync<TreatmentDetailResponse>())!;

        var updateDetail = await client.PutAsJsonAsync($"/treatments/{treatment.Id}/details/{detail.Id}",
            new UpdateTreatmentDetailRequest("Sesión RBAC editada", "Progreso editado", null, DateTime.UtcNow));
        updateDetail.StatusCode.Should().Be(HttpStatusCode.OK, "update treatment detail must succeed for this role");

        var createTxComment = await client.PostAsJsonAsync($"/treatments/{treatment.Id}/comments",
            new CreateTreatmentCommentRequest("Comentario RBAC", CommentType.Medical));
        createTxComment.StatusCode.Should().Be(HttpStatusCode.Created, "create treatment comment must succeed for this role");
        var treatmentComment = (await createTxComment.Content.ReadFromJsonAsync<TreatmentCommentResponse>())!;

        var updateTxComment = await client.PutAsJsonAsync(
            $"/treatments/{treatment.Id}/comments/{treatmentComment.Id}",
            new UpdateTreatmentCommentRequest("Comentario RBAC editado", CommentType.Medical, CommentStatus.Accepted));
        updateTxComment.StatusCode.Should().Be(HttpStatusCode.OK, "update treatment comment must succeed for this role");

        var createPtComment = await client.PostAsJsonAsync($"/patients/{patient.Id}/comments",
            new CreatePatientCommentRequest("Nota RBAC", CommentType.Simple));
        createPtComment.StatusCode.Should().Be(HttpStatusCode.Created, "create patient comment must succeed for this role");
        var patientComment = (await createPtComment.Content.ReadFromJsonAsync<PatientCommentResponse>())!;

        var updatePtComment = await client.PutAsJsonAsync($"/patients/{patient.Id}/comments/{patientComment.Id}",
            new UpdatePatientCommentRequest("Nota RBAC editada", CommentType.Simple, CommentStatus.Accepted));
        updatePtComment.StatusCode.Should().Be(HttpStatusCode.OK, "update patient comment must succeed for this role");

        return new PatientGraph(patient.Id, treatment.Id, detail.Id, treatmentComment.Id, patientComment.Id);
    }

    private static CreatePatientRequest ValidPatientRequest(string firstName) =>
        new(firstName, "Rbac", null, new DateTime(2015, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            Gender.Female, "Nicaragua", null, "Managua", "Dirección RBAC", null,
            AttentionType.Medical, null, null, null);

    private static UpdatePatientRequest ValidUpdatePatientRequest(string firstName) =>
        new(firstName, "Rbac", null, new DateTime(2015, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            Gender.Female, "Nicaragua", null, "Managua", "Dirección RBAC editada", null,
            AttentionType.Medical, null, null, null);

    private async Task<Guid> SeedUserAsync(string roleName, string? email = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HouseCenterDbContext>();
        var role = await db.Roles.SingleAsync(r => r.Name == roleName);

        var user = new User
        {
            FirstName = roleName,
            LastName = "Rbac",
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
}
