using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Constants;
using HouseCenter.Api.Domain.Entities;
using HouseCenter.Api.Features.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HouseCenter.Api.Tests;

// HTTP-level equivalents of the PR3 session-management unit tests (UserSessionServiceTests)
// — the endpoint/serialization/auth-middleware layer those pure-C# tests can't cover:
// wire-level 204/404/401 codes, ClaimsPrincipal-based self-scoping through real JWTs, and
// the actual JSON shape of GET /auth/sessions. No time control needed anywhere in this
// file, so all tests use the shared `[Collection("Api")]` fixture.
[Collection("Api")]
public sealed class SessionManagementTests(ApiFactory factory)
{
    private const string Password = "Test123!pass";

    // The API serializes enums as their string name (Program.cs: JsonStringEnumConverter
    // registered on ConfigureHttpJsonOptions for request bodies) — a plain HttpClient JSON
    // read needs the same converter client-side to deserialize SessionResponse.Platform.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    // ─── Logout (R7) ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Logout_RevokesOnlyThatDevice()
    {
        var (_, email, password) = await SeedMemberUserAsync();
        var (clientA, tokensA) = await LoginAsync(email, password, "device-A");
        var (clientB, tokensB) = await LoginAsync(email, password, "device-B");

        var logoutResponse = await clientA.PostAsJsonAsync("/auth/logout", new LogoutRequest("device-A"));
        logoutResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var refreshA = await clientA.PostAsJsonAsync("/auth/refresh",
            new RefreshRequest(tokensA.RefreshToken, "device-A"));
        refreshA.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var refreshB = await clientB.PostAsJsonAsync("/auth/refresh",
            new RefreshRequest(tokensB.RefreshToken, "device-B"));
        refreshB.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ─── Revoke-all (R7) ────────────────────────────────────────────────────────

    [Fact]
    public async Task RevokeAll_RevokesEveryDevice()
    {
        var (_, email, password) = await SeedMemberUserAsync();
        var (clientA, tokensA) = await LoginAsync(email, password, "device-A");
        var (clientB, tokensB) = await LoginAsync(email, password, "device-B");

        var revokeAllResponse = await clientA.PostAsync("/auth/sessions/revoke-all", null);
        revokeAllResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var refreshA = await clientA.PostAsJsonAsync("/auth/refresh",
            new RefreshRequest(tokensA.RefreshToken, "device-A"));
        refreshA.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var refreshB = await clientB.PostAsJsonAsync("/auth/refresh",
            new RefreshRequest(tokensB.RefreshToken, "device-B"));
        refreshB.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─── Per-device DELETE + anti-enumeration (R7) ─────────────────────────────

    [Fact]
    public async Task DeleteSession_RevokesOnlyThatDevice()
    {
        var (_, email, password) = await SeedMemberUserAsync();
        var (clientA, tokensA) = await LoginAsync(email, password, "device-A");
        var (clientB, tokensB) = await LoginAsync(email, password, "device-B");

        var idB = (await GetSessionsAsync(clientA)).Single(s => s.DeviceId == "device-B").Id;

        var deleteResponse = await clientA.DeleteAsync($"/auth/sessions/{idB}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await clientB.PostAsJsonAsync("/auth/refresh", new RefreshRequest(tokensB.RefreshToken, "device-B")))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await clientA.PostAsJsonAsync("/auth/refresh", new RefreshRequest(tokensA.RefreshToken, "device-A")))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteSession_ForeignOrNonexistentId_Returns404_NeverForbidden()
    {
        var (_, emailA, passwordA) = await SeedMemberUserAsync();
        var (_, emailB, passwordB) = await SeedMemberUserAsync();
        var (clientA, _) = await LoginAsync(emailA, passwordA, "device-A");
        var (clientB, tokensB) = await LoginAsync(emailB, passwordB, "device-B");

        var foreignSessionId = (await GetSessionsAsync(clientB)).Single().Id;

        // Anti-enumeration: another user's session id -> 404, not 403.
        (await clientA.DeleteAsync($"/auth/sessions/{foreignSessionId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await clientA.DeleteAsync($"/auth/sessions/{Guid.NewGuid()}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        // B's session is completely untouched by A's failed attempt.
        (await clientB.PostAsJsonAsync("/auth/refresh", new RefreshRequest(tokensB.RefreshToken, "device-B")))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ─── GET /auth/sessions — self-scoping + hash-free serialization (R7, R9) ──

    [Fact]
    public async Task GetSessions_SelfScopedAndNeverExposesHashFields()
    {
        var (_, emailA, passwordA) = await SeedMemberUserAsync();
        var (_, emailB, passwordB) = await SeedMemberUserAsync();
        var (clientA, _) = await LoginAsync(emailA, passwordA, "device-A");
        await LoginAsync(emailB, passwordB, "device-B");

        var response = await clientA.GetAsync("/auth/sessions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var raw = await response.Content.ReadAsStringAsync();

        // Serialization contract (R9) — assert against the raw JSON text, not just the
        // deserialized DTO shape, so a future accidental field addition is actually caught.
        raw.Should().NotContainEquivalentOf("currentTokenHash");
        raw.Should().NotContainEquivalentOf("previousTokenHash");
        raw.Should().NotContainEquivalentOf("TokenHash");

        var sessions = JsonSerializer.Deserialize<List<SessionResponse>>(raw, JsonOptions)!;
        sessions.Should().ContainSingle(s => s.DeviceId == "device-A");
        sessions.Should().NotContain(s => s.DeviceId == "device-B");
    }

    // ─── Unauthenticated access (R7) ────────────────────────────────────────────

    [Fact]
    public async Task SessionEndpoints_WithoutAuth_Return401()
    {
        var client = factory.CreateClient();

        (await client.GetAsync("/auth/sessions"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/auth/logout", new LogoutRequest("device-A")))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.DeleteAsync($"/auth/sessions/{Guid.NewGuid()}"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsync("/auth/sessions/revoke-all", null))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─── Password reset bulk-revoke (R8) — first end-to-end coverage of ResetPasswordAsync ──

    [Fact]
    public async Task ResetPassword_RevokesAllSessions()
    {
        var (userId, email, password) = await SeedMemberUserAsync();
        var (clientA, tokensA) = await LoginAsync(email, password, "device-A");
        var (clientB, tokensB) = await LoginAsync(email, password, "device-B");

        var resetToken = await CreatePasswordResetTokenAsync(userId);
        var resetResponse = await clientA.PostAsJsonAsync("/auth/password/reset",
            new PasswordResetDto(resetToken, "BrandNewPassword123!"));
        // ResetPasswordAsync returns a bare Result (no value) -> ToHttpResult() maps
        // success to 204 NoContent, not 200 (see ResultExtensions.ToHttpResult()).
        resetResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await clientA.PostAsJsonAsync("/auth/refresh", new RefreshRequest(tokensA.RefreshToken, "device-A")))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await clientB.PostAsJsonAsync("/auth/refresh", new RefreshRequest(tokensB.RefreshToken, "device-B")))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────

    private async Task<(Guid UserId, string Email, string Password)> SeedMemberUserAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HouseCenterDbContext>();
        var role = await db.Roles.SingleAsync(r => r.Name == RoleNames.Member);

        var email = $"session-mgmt-{Guid.NewGuid():N}@test.local";
        var user = new User
        {
            FirstName = "Session",
            LastName = "Mgmt",
            Email = email,
            HashedPassword = BCrypt.Net.BCrypt.HashPassword(Password),
            Address = "Test 123",
            EmailConfirmed = true,
            Roles = [role]
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (user.Id, email, Password);
    }

    private async Task<(HttpClient Client, TokenPairResponse Tokens)> LoginAsync(
        string email, string password, string deviceId)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/auth/login",
            new LoginRequest(email, password, deviceId, null, DevicePlatform.Web));
        response.EnsureSuccessStatusCode();

        var tokens = (await response.Content.ReadFromJsonAsync<TokenPairResponse>())!;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return (client, tokens);
    }

    private static async Task<List<SessionResponse>> GetSessionsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<SessionResponse>>("/auth/sessions", JsonOptions))!;

    private async Task<string> CreatePasswordResetTokenAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HouseCenterDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        return tokenService.CreatePasswordResetToken(user);
    }
}
