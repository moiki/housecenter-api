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
using HouseCenter.Api.Features.Consultations;
using HouseCenter.Api.Features.Notifications;
using HouseCenter.Api.Features.Patients;
using HouseCenter.Api.Features.Patients.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HouseCenter.Api.Tests;

[Collection("Api")]
public sealed class NotificationTests(ApiFactory factory)
{
    private const string Password = "Test123!pass";

    private Task<HttpClient> OwnerClient() => factory.CreateAuthenticatedClientAsync();

    // ─── Event → notification ─────────────────────────────────────────────────

    [Fact]
    public async Task OpenConsultation_NotifiesAssignedDoctor()
    {
        var owner = await OwnerClient();
        var patient = await CreatePatientAsync(owner);
        var (doctorClient, doctorId) = await LoginUserWithRoleAsync(RoleNames.Doctor);
        await AssignDoctorAsync(owner, patient.Id, doctorId);

        await OpenConsultationAsync(owner, patient.Id, doctorId);

        var notes = await ListAsync(doctorClient, unreadOnly: true);
        notes.Items.Should().Contain(n => n.Type == "ConsultationOpened");
    }

    [Fact]
    public async Task PostMessage_NotifiesOtherParticipant_NotAuthor()
    {
        var owner = await OwnerClient();
        var patient = await CreatePatientAsync(owner);
        var (doctorClient, doctorId) = await LoginUserWithRoleAsync(RoleNames.Doctor);
        await AssignDoctorAsync(owner, patient.Id, doctorId);
        var consultation = await OpenConsultationAsync(owner, patient.Id, doctorId);

        // Doctor (a participant) posts — the opener should hear about it, the doctor shouldn't.
        var post = await doctorClient.PostAsJsonAsync(
            $"/consultations/{consultation.Id}/messages",
            new PostMessageRequest("Revisado, continúen el tratamiento.", null));
        post.EnsureSuccessStatusCode();

        var ownerNotes = await ListAsync(owner, unreadOnly: false);
        ownerNotes.Items.Should().Contain(n =>
            n.Type == "ConsultationMessage" && n.ReferenceId == consultation.Id);

        var doctorNotes = await ListAsync(doctorClient, unreadOnly: false);
        doctorNotes.Items.Should().NotContain(n => n.Type == "ConsultationMessage");
    }

    [Fact]
    public async Task ResolveConsultation_NotifiesOpener()
    {
        var owner = await OwnerClient();
        var patient = await CreatePatientAsync(owner);
        var (doctorClient, doctorId) = await LoginUserWithRoleAsync(RoleNames.Doctor);
        await AssignDoctorAsync(owner, patient.Id, doctorId);
        var consultation = await OpenConsultationAsync(owner, patient.Id, doctorId);

        var resolve = await doctorClient.PatchAsJsonAsync(
            $"/consultations/{consultation.Id}/status",
            new UpdateConsultationStatusRequest(ConsultationStatus.Resolved));
        resolve.EnsureSuccessStatusCode();

        var ownerNotes = await ListAsync(owner, unreadOnly: false);
        ownerNotes.Items.Should().Contain(n =>
            n.Type == "ConsultationResolved" && n.ReferenceId == consultation.Id);
    }

    // ─── Read API ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListNotifications_UnreadOnly_ReturnsUnread()
    {
        var owner = await OwnerClient();
        var patient = await CreatePatientAsync(owner);
        var (doctorClient, doctorId) = await LoginUserWithRoleAsync(RoleNames.Doctor);
        await AssignDoctorAsync(owner, patient.Id, doctorId);
        await OpenConsultationAsync(owner, patient.Id, doctorId);

        var unread = await ListAsync(doctorClient, unreadOnly: true);
        unread.Items.Should().NotBeEmpty();
        unread.Items.Should().OnlyContain(n => !n.IsRead);
    }

    [Fact]
    public async Task MarkRead_DecrementsUnreadCount()
    {
        var owner = await OwnerClient();
        var patient = await CreatePatientAsync(owner);
        var (doctorClient, doctorId) = await LoginUserWithRoleAsync(RoleNames.Doctor);
        await AssignDoctorAsync(owner, patient.Id, doctorId);
        await OpenConsultationAsync(owner, patient.Id, doctorId);

        var before = await UnreadCountAsync(doctorClient);
        before.Should().BeGreaterThanOrEqualTo(1);

        var first = (await ListAsync(doctorClient, unreadOnly: true)).Items[0];
        var read = await doctorClient.PatchAsync($"/notifications/{first.Id}/read", null);
        read.StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await UnreadCountAsync(doctorClient);
        after.Should().Be(before - 1);
    }

    [Fact]
    public async Task MarkRead_OnAnotherUsersNotification_Returns404()
    {
        var owner = await OwnerClient();
        var patient = await CreatePatientAsync(owner);
        var (doctorClient, doctorId) = await LoginUserWithRoleAsync(RoleNames.Doctor);
        await AssignDoctorAsync(owner, patient.Id, doctorId);
        await OpenConsultationAsync(owner, patient.Id, doctorId);

        // The doctor's notification id, accessed by a different user (another doctor)
        var doctorNote = (await ListAsync(doctorClient, unreadOnly: true)).Items[0];
        var (otherClient, _) = await LoginUserWithRoleAsync(RoleNames.Doctor);

        var response = await otherClient.PatchAsync($"/notifications/{doctorNote.Id}/read", null);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MarkAllRead_ClearsUnreadCount()
    {
        var owner = await OwnerClient();
        var patient = await CreatePatientAsync(owner);
        var (doctorClient, doctorId) = await LoginUserWithRoleAsync(RoleNames.Doctor);
        await AssignDoctorAsync(owner, patient.Id, doctorId);
        await OpenConsultationAsync(owner, patient.Id, doctorId);
        await OpenConsultationAsync(owner, patient.Id, doctorId);

        (await UnreadCountAsync(doctorClient)).Should().BeGreaterThanOrEqualTo(2);

        var readAll = await doctorClient.PostAsync("/notifications/read-all", null);
        readAll.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await UnreadCountAsync(doctorClient)).Should().Be(0);
    }

    // ─── Email channel (Phase 2) ──────────────────────────────────────────────

    [Fact]
    public async Task OpenConsultation_EmailsAssignedDoctor()
    {
        var owner = await OwnerClient();
        var patient = await CreatePatientAsync(owner);
        var (_, doctorId) = await LoginUserWithRoleAsync(RoleNames.Doctor);
        var doctorEmail = await GetUserEmailAsync(doctorId);
        await AssignDoctorAsync(owner, patient.Id, doctorId);

        await OpenConsultationAsync(owner, patient.Id, doctorId);

        factory.Emails.Sent.Should().Contain(e =>
            e.To == doctorEmail && e.Subject == "New consultation assigned");
    }

    [Fact]
    public async Task EmailFailure_DoesNotBreakAction_AndInAppStillCreated()
    {
        var owner = await OwnerClient();
        var patient = await CreatePatientAsync(owner);
        var (doctorClient, doctorId) = await LoginUserWithRoleAsync(RoleNames.Doctor);
        await AssignDoctorAsync(owner, patient.Id, doctorId);

        factory.Emails.ThrowOnSend = true;
        try
        {
            // Best-effort: the email channel throwing must not fail the consultation
            // action, and the in-app notification must still be persisted.
            var consultation = await OpenConsultationAsync(owner, patient.Id, doctorId);
            consultation.Should().NotBeNull();

            var notes = await ListAsync(doctorClient, unreadOnly: true);
            notes.Items.Should().Contain(n => n.Type == "ConsultationOpened");
        }
        finally
        {
            factory.Emails.ThrowOnSend = false;
        }
    }

    // ─── Push channel (Phase 4) ────────────────────────────────────────────────

    [Fact]
    public async Task RegisterPushSubscription_Returns204()
    {
        var (memberClient, _) = await LoginUserWithRoleAsync(RoleNames.Member);

        var response = await memberClient.PostAsJsonAsync("/notifications/push-subscriptions",
            new RegisterPushSubscriptionRequest($"tok-{Guid.NewGuid():N}", PushPlatform.Android));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task RemovePushSubscription_Returns204()
    {
        var (memberClient, _) = await LoginUserWithRoleAsync(RoleNames.Member);
        var token = $"tok-{Guid.NewGuid():N}";
        await memberClient.PostAsJsonAsync("/notifications/push-subscriptions",
            new RegisterPushSubscriptionRequest(token, PushPlatform.Android));

        var response = await memberClient.DeleteAsync($"/notifications/push-subscriptions/{token}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task OpenConsultation_PushesToRegisteredDoctorToken()
    {
        var owner = await OwnerClient();
        var patient = await CreatePatientAsync(owner);
        var (doctorClient, doctorId) = await LoginUserWithRoleAsync(RoleNames.Doctor);
        var token = $"tok-{Guid.NewGuid():N}";
        await RegisterPushAsync(doctorClient, token);
        await AssignDoctorAsync(owner, patient.Id, doctorId);

        await OpenConsultationAsync(owner, patient.Id, doctorId);

        factory.Push.Sent.Should().Contain(p => p.Token == token);
    }

    [Fact]
    public async Task Push_InvalidToken_IsPruned()
    {
        var owner = await OwnerClient();
        var patient = await CreatePatientAsync(owner);
        var (doctorClient, doctorId) = await LoginUserWithRoleAsync(RoleNames.Doctor);
        var token = $"tok-{Guid.NewGuid():N}";
        await RegisterPushAsync(doctorClient, token);
        await AssignDoctorAsync(owner, patient.Id, doctorId);

        factory.Push.InvalidTokens.Add(token);
        try
        {
            await OpenConsultationAsync(owner, patient.Id, doctorId);

            // The handler should have deleted the dead token.
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<HouseCenterDbContext>();
            (await db.PushSubscriptions.AnyAsync(s => s.Token == token)).Should().BeFalse();
        }
        finally
        {
            factory.Push.InvalidTokens.Remove(token);
        }
    }

    // ─── Session reminders (Phase 3) ──────────────────────────────────────────

    [Fact]
    public async Task MarkSessionMissed_NotifiesCollaborator()
    {
        var owner = await OwnerClient();
        var patient = await CreatePatientAsync(owner);
        var clinic = await CreateClinicAsync(owner);
        var (memberClient, memberId) = await LoginUserWithRoleAsync(RoleNames.Member);
        var session = await CreateSessionAsync(owner, patient.Id, memberId, clinic.Id, DateTime.UtcNow);

        var patch = await owner.PatchAsJsonAsync(
            $"/patients/{patient.Id}/sessions/{session.Id}/status",
            new UpdateSessionStatusRequest(SessionStatus.Missed, null, null));
        patch.EnsureSuccessStatusCode();

        var notes = await ListAsync(memberClient, unreadOnly: false);
        notes.Items.Should().Contain(n => n.Type == "SessionMissed" && n.ReferenceId == session.Id);
    }

    [Fact]
    public async Task Scanner_NotifiesUpcomingSession_AndIsIdempotent()
    {
        var owner = await OwnerClient();
        var patient = await CreatePatientAsync(owner);
        var clinic = await CreateClinicAsync(owner);
        var (memberClient, memberId) = await LoginUserWithRoleAsync(RoleNames.Member);
        // Inside the 24h window so the scanner picks it up.
        var session = await CreateSessionAsync(owner, patient.Id, memberId, clinic.Id, DateTime.UtcNow.AddHours(2));

        await RunScannerAsync();

        var afterFirst = (await ListAsync(memberClient, unreadOnly: false)).Items
            .Count(n => n.Type == "SessionUpcoming" && n.ReferenceId == session.Id);
        afterFirst.Should().Be(1);

        // Second scan must not re-remind the same session (ReminderSentAt dedup).
        await RunScannerAsync();

        var afterSecond = (await ListAsync(memberClient, unreadOnly: false)).Items
            .Count(n => n.Type == "SessionUpcoming" && n.ReferenceId == session.Id);
        afterSecond.Should().Be(1);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static async Task RegisterPushAsync(HttpClient client, string token)
    {
        var response = await client.PostAsJsonAsync("/notifications/push-subscriptions",
            new RegisterPushSubscriptionRequest(token, PushPlatform.Android));
        response.EnsureSuccessStatusCode();
    }

    private async Task RunScannerAsync()
    {
        using var scope = factory.Services.CreateScope();
        var scanner = scope.ServiceProvider.GetRequiredService<SessionReminderScanner>();
        await scanner.ScanAndNotifyAsync(CancellationToken.None);
    }

    private static async Task<ClinicResponse> CreateClinicAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/clinics",
            new CreateClinicRequest($"Notif {Guid.NewGuid():N}"[..25], "Calle 1"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ClinicResponse>())!;
    }

    private static async Task<AttentionSessionResponse> CreateSessionAsync(
        HttpClient client, Guid patientId, Guid collaboratorId, Guid clinicId, DateTime sessionDate)
    {
        var response = await client.PostAsJsonAsync($"/patients/{patientId}/sessions",
            new CreateAttentionSessionRequest(
                collaboratorId, clinicId, null, AttentionType.Medical, sessionDate, null, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AttentionSessionResponse>())!;
    }

    private async Task<string> GetUserEmailAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HouseCenterDbContext>();
        return await db.Users.Where(u => u.Id == userId).Select(u => u.Email).SingleAsync();
    }

    private static async Task<PagedResult<NotificationResponse>> ListAsync(HttpClient client, bool unreadOnly)
    {
        var response = await client.GetAsync($"/notifications?unreadOnly={unreadOnly.ToString().ToLower()}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PagedResult<NotificationResponse>>())!;
    }

    private static async Task<int> UnreadCountAsync(HttpClient client)
    {
        var response = await client.GetAsync("/notifications/unread-count");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UnreadCountResponse>())!.Count;
    }

    private async Task<Guid> SeedUserAsync(string roleName, string? email = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HouseCenterDbContext>();
        var role = await db.Roles.SingleAsync(r => r.Name == roleName);

        var user = new User
        {
            FirstName = roleName,
            LastName = "Notif",
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
        var response = await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, Password));
        response.EnsureSuccessStatusCode();

        var tokens = await response.Content.ReadFromJsonAsync<TokenPairResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return (client, userId);
    }

    private static async Task<PatientResponse> CreatePatientAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/patients", new CreatePatientRequest(
            $"Notif{Guid.NewGuid():N}"[..20], "Paciente", null,
            new DateTime(2016, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            Gender.Male, "Nicaragua", null, "Managua", "Dirección 1", null,
            AttentionType.Medical, null, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PatientResponse>())!;
    }

    private static async Task AssignDoctorAsync(HttpClient ownerClient, Guid patientId, Guid doctorId)
    {
        var response = await ownerClient.PostAsync($"/patients/{patientId}/doctors/{doctorId}", null);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<ConsultationResponse> OpenConsultationAsync(
        HttpClient client, Guid patientId, Guid doctorId)
    {
        var response = await client.PostAsJsonAsync("/consultations", new CreateConsultationRequest(
            patientId, null, doctorId, "Seguimiento", "Primer reporte", null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ConsultationResponse>())!;
    }
}
