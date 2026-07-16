using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Constants;
using HouseCenter.Api.Domain.Entities;
using HouseCenter.Api.Features.Attachments;
using HouseCenter.Api.Features.Auth;
using HouseCenter.Api.Features.Clinics;
using HouseCenter.Api.Features.Patients;
using HouseCenter.Api.Features.Patients.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HouseCenter.Api.Tests;

[Collection("Api")]
public sealed class AttachmentTests(ApiFactory factory)
{
    private const string Password = "Test123!pass";
    private static readonly byte[] SampleJpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5, 6, 7, 8];

    private Task<HttpClient> OwnerClient() => factory.CreateAuthenticatedClientAsync();

    // ─── Upload ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Upload_ToSession_AsCollaborator_Returns201()
    {
        var owner = await OwnerClient();
        var (patient, sessionId, memberClient) = await CreateSessionWithMemberClientAsync(owner);

        var response = await UploadAsync(memberClient, "AttentionSession", sessionId,
            SampleJpeg, "visita.jpg", "image/jpeg");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var attachment = await response.Content.ReadFromJsonAsync<AttachmentResponse>();
        attachment!.FileName.Should().Be("visita.jpg");
        attachment.OwnerType.Should().Be("AttentionSession");
        attachment.SizeBytes.Should().Be(SampleJpeg.Length);
    }

    [Fact]
    public async Task Upload_OversizeFile_Returns400()
    {
        var owner = await OwnerClient();
        var patient = await CreatePatientAsync(owner);

        var big = new byte[70_000]; // over the 65536 test cap
        var response = await UploadAsync(owner, "Patient", patient.Id, big, "big.jpg", "image/jpeg");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Upload_DisallowedContentType_Returns400()
    {
        var owner = await OwnerClient();
        var patient = await CreatePatientAsync(owner);

        var response = await UploadAsync(owner, "Patient", patient.Id,
            [1, 2, 3], "evil.exe", "application/x-msdownload");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ─── Download ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Download_ReturnsStoredBytes()
    {
        var owner = await OwnerClient();
        var patient = await CreatePatientAsync(owner);
        var uploaded = await UploadOkAsync(owner, "Patient", patient.Id, SampleJpeg, "x.jpg", "image/jpeg");

        var response = await owner.GetAsync(uploaded.DownloadUrl);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(SampleJpeg);
    }

    [Fact]
    public async Task Download_AsSponsor_Returns404()
    {
        var owner = await OwnerClient();
        var patient = await CreatePatientAsync(owner);
        var uploaded = await UploadOkAsync(owner, "Patient", patient.Id, SampleJpeg, "x.jpg", "image/jpeg");
        var (sponsorClient, _) = await LoginUserWithRoleAsync(RoleNames.Sponsor);

        var response = await sponsorClient.GetAsync(uploaded.DownloadUrl);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─── List + delete ──────────────────────────────────────────────────────────

    [Fact]
    public async Task List_ByOwner_ReturnsUploaded()
    {
        var owner = await OwnerClient();
        var (patient, sessionId, memberClient) = await CreateSessionWithMemberClientAsync(owner);
        var uploaded = await UploadOkAsync(memberClient, "AttentionSession", sessionId, SampleJpeg, "a.jpg", "image/jpeg");

        var list = await memberClient.GetFromJsonAsync<List<AttachmentResponse>>(
            $"/attachments?ownerType=AttentionSession&ownerId={sessionId}");

        list.Should().NotBeNull();
        list!.Should().ContainSingle(a => a.Id == uploaded.Id);
    }

    [Fact]
    public async Task Delete_ByUploader_ThenDownload_Returns404()
    {
        var owner = await OwnerClient();
        var patient = await CreatePatientAsync(owner);
        var uploaded = await UploadOkAsync(owner, "Patient", patient.Id, SampleJpeg, "x.jpg", "image/jpeg");

        var del = await owner.DeleteAsync($"/attachments/{uploaded.Id}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await owner.GetAsync(uploaded.DownloadUrl);
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client, string ownerType, Guid ownerId, byte[] bytes, string fileName, string contentType)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", fileName);
        return await client.PostAsync($"/attachments?ownerType={ownerType}&ownerId={ownerId}", content);
    }

    private static async Task<AttachmentResponse> UploadOkAsync(
        HttpClient client, string ownerType, Guid ownerId, byte[] bytes, string fileName, string contentType)
    {
        var response = await UploadAsync(client, ownerType, ownerId, bytes, fileName, contentType);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AttachmentResponse>())!;
    }

    private async Task<Guid> SeedUserAsync(string roleName, string? email = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HouseCenterDbContext>();
        var role = await db.Roles.SingleAsync(r => r.Name == roleName);
        var user = new User
        {
            FirstName = roleName, LastName = "Attach",
            Email = email ?? $"{roleName.ToLower()}-{Guid.NewGuid():N}@test.local",
            HashedPassword = BCrypt.Net.BCrypt.HashPassword(Password),
            Address = "Test 1", EmailConfirmed = true, Roles = [role]
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
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return (client, userId);
    }

    private static async Task<PatientResponse> CreatePatientAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/patients", new CreatePatientRequest(
            $"Attach{Guid.NewGuid():N}"[..20], "Paciente", null,
            new DateTime(2016, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            Gender.Male, "Nicaragua", null, "Managua", "Dir 1", null,
            AttentionType.Medical, null, null, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PatientResponse>())!;
    }

    private static async Task<ClinicResponse> CreateClinicAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/clinics",
            new CreateClinicRequest($"Attach {Guid.NewGuid():N}"[..25], "Calle 1"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ClinicResponse>())!;
    }

    private async Task<(PatientResponse Patient, Guid SessionId, HttpClient MemberClient)>
        CreateSessionWithMemberClientAsync(HttpClient owner)
    {
        var patient = await CreatePatientAsync(owner);
        var clinic = await CreateClinicAsync(owner);
        var (memberClient, memberId) = await LoginUserWithRoleAsync(RoleNames.Member);
        var create = await owner.PostAsJsonAsync($"/patients/{patient.Id}/sessions",
            new CreateAttentionSessionRequest(
                memberId, clinic.Id, null, AttentionType.Medical, DateTime.UtcNow, null, null));
        create.EnsureSuccessStatusCode();
        var session = (await create.Content.ReadFromJsonAsync<AttentionSessionResponse>())!;
        return (patient, session.Id, memberClient);
    }
}
