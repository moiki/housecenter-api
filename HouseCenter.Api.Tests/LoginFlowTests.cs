using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Entities;
using HouseCenter.Api.Features.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HouseCenter.Api.Tests;

[Collection("Api")]
public sealed class LoginFlowTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private const string DeviceId = "login-flow-tests-device";

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsTokenPair()
    {
        var response = await _client.PostAsJsonAsync("/auth/login",
            new LoginRequest(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword, DeviceId, "Owner Laptop", DevicePlatform.Web));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var tokens = await response.Content.ReadFromJsonAsync<TokenPairResponse>();
        tokens!.AccessToken.Should().NotBeNullOrWhiteSpace();
        tokens.RefreshToken.Should().NotBeNullOrWhiteSpace();
        tokens.AccessToken.Should().NotBe(tokens.RefreshToken);
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/auth/login",
            new LoginRequest(ApiFactory.OwnerEmail, "definitely-wrong-password", DeviceId, null, DevicePlatform.Web));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithMalformedEmail_Returns400ValidationProblem()
    {
        var response = await _client.PostAsJsonAsync("/auth/login",
            new LoginRequest("not-an-email", "whatever", DeviceId, null, DevicePlatform.Web));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // PR2 (device-bound-sessions, R10, verify WARNING 1 closeout): "A request missing
    // deviceId MUST fail validation with 400." LoginRequestValidator.RuleFor(x => x.DeviceId)
    // is NotEmpty() — an empty string is the JSON-representable form of "missing" for a
    // non-nullable string field, and is what NotEmpty() actually enforces.
    [Fact]
    public async Task Login_MissingDeviceId_Returns400ValidationProblem()
    {
        var response = await _client.PostAsJsonAsync("/auth/login",
            new LoginRequest(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword, string.Empty, null, DevicePlatform.Web));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Errors.Should().ContainKey(nameof(LoginRequest.DeviceId));
    }

    // PR2 (device-bound-sessions, R10): LoginRequestValidator.RuleFor(x => x.Platform).IsInEnum().
    // DevicePlatform serializes as a JSON string name (Program.cs registers a global
    // JsonStringEnumConverter), but that converter's default `allowIntegerValues: true` still
    // lets an out-of-range NUMBER through model binding as an undefined enum value (e.g.
    // (DevicePlatform)999) without throwing — so raw JSON with an out-of-range integer is the
    // way to reach IsInEnum() as a validation failure rather than a JSON-deserialization
    // exception. Sending an unrecognized STRING (e.g. "Toaster") would instead fail during
    // body deserialization before the validator ever runs, which is a different failure mode
    // than what R10 (validator-level 400) is testing.
    [Fact]
    public async Task Login_InvalidPlatform_Returns400ValidationProblem()
    {
        var payload = new
        {
            Email = ApiFactory.OwnerEmail,
            Password = ApiFactory.OwnerPassword,
            DeviceId,
            DeviceName = (string?)null,
            Platform = 999
        };

        var response = await _client.PostAsJsonAsync("/auth/login", payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Errors.Should().ContainKey(nameof(LoginRequest.Platform));
    }

    // PR2 (device-bound-sessions, R10): RefreshRequestValidator.RuleFor(x => x.DeviceId) is
    // NotEmpty() too — same shape of proof as the login case above, for /auth/refresh.
    [Fact]
    public async Task Refresh_MissingDeviceId_Returns400ValidationProblem()
    {
        var tokens = await LoginAsOwnerAsync();

        var response = await _client.PostAsJsonAsync("/auth/refresh",
            new RefreshRequest(tokens.RefreshToken, string.Empty));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Errors.Should().ContainKey(nameof(RefreshRequest.DeviceId));
    }

    [Fact]
    public async Task Me_WithValidAccessToken_ReturnsOwnerProfile()
    {
        var tokens = await LoginAsOwnerAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, "/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var me = await response.Content.ReadFromJsonAsync<MeResponse>();
        me!.Email.Should().Be(ApiFactory.OwnerEmail);
        me.Roles.Should().Contain("Owner");
    }

    [Fact]
    public async Task Me_WithoutToken_Returns401()
    {
        var response = await _client.GetAsync("/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_WithValidRefreshToken_ReturnsNewTokenPair()
    {
        var tokens = await LoginAsOwnerAsync();

        var response = await _client.PostAsJsonAsync("/auth/refresh",
            new RefreshRequest(tokens.RefreshToken, DeviceId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var refreshed = await response.Content.ReadFromJsonAsync<TokenPairResponse>();
        refreshed!.AccessToken.Should().NotBeNullOrWhiteSpace();
        refreshed.RefreshToken.Should().NotBeNullOrWhiteSpace();
        // Single-use rotation: the presented token must never come back unchanged
        refreshed.RefreshToken.Should().NotBe(tokens.RefreshToken);
    }

    [Fact]
    public async Task Refresh_WithAccessTokenInsteadOfRefreshToken_Returns401()
    {
        var tokens = await LoginAsOwnerAsync();

        // Cross-use protection: an access token must never match any session's token hash
        var response = await _client.PostAsJsonAsync("/auth/refresh",
            new RefreshRequest(tokens.AccessToken, DeviceId));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // PR4 (device-bound-sessions, R1/R5): a token that never traces to any UserSession row
    // at all — not the access-token cross-use case above, just arbitrary noise — must also
    // 401. No time control needed; belongs on the shared fixture.
    [Fact]
    public async Task Refresh_WithGarbageToken_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/auth/refresh",
            new RefreshRequest("this-string-matches-no-session-hash-at-all", DeviceId));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // R1, R3 — proves the wire-level rotation contract AND the DB-level hash shift that the
    // pure-C# UserSessionServiceTests (PR2/PR3) can't observe: hashing happens behind the
    // HTTP boundary via the real ITokenHasher/DbContext wiring, not the raw service call.
    [Fact]
    public async Task Refresh_RotatesToken_PreviousAndCurrentHashesUpdate()
    {
        var tokens = await LoginAsOwnerAsync();

        var response = await _client.PostAsJsonAsync("/auth/refresh",
            new RefreshRequest(tokens.RefreshToken, DeviceId));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rotated = (await response.Content.ReadFromJsonAsync<TokenPairResponse>())!;

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HouseCenterDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<ITokenHasher>();

        var session = await db.UserSessions.AsNoTracking()
            .SingleAsync(s => s.CurrentTokenHash == hasher.Hash(rotated.RefreshToken));
        session.PreviousTokenHash.Should().Be(hasher.Hash(tokens.RefreshToken));
    }

    // R4 — a lost-response retry within the default 30s grace window must still succeed.
    // No time control needed: the two HTTP calls happen milliseconds apart, well inside the
    // window, so this runs safely on the shared fixture.
    [Fact]
    public async Task Refresh_WithinGraceWindow_Accepted()
    {
        var tokens = await LoginAsOwnerAsync();

        var firstRotation = await _client.PostAsJsonAsync("/auth/refresh",
            new RefreshRequest(tokens.RefreshToken, DeviceId));
        firstRotation.EnsureSuccessStatusCode();
        var t2 = (await firstRotation.Content.ReadFromJsonAsync<TokenPairResponse>())!;

        // Present the just-superseded token (now Previous) again — still within grace.
        var graceRetry = await _client.PostAsJsonAsync("/auth/refresh",
            new RefreshRequest(tokens.RefreshToken, DeviceId));

        graceRetry.StatusCode.Should().Be(HttpStatusCode.OK);
        var t3 = await graceRetry.Content.ReadFromJsonAsync<TokenPairResponse>();
        t3!.RefreshToken.Should().NotBe(tokens.RefreshToken);
        t3.RefreshToken.Should().NotBe(t2.RefreshToken);
    }

    // R5 — a deviceId that doesn't match the session's bound device revokes the WHOLE
    // session immediately, even for the correct deviceId's retry right after.
    [Fact]
    public async Task Refresh_DeviceIdMismatch_RevokesSession401()
    {
        var tokens = await LoginAsOwnerAsync();

        var mismatch = await _client.PostAsJsonAsync("/auth/refresh",
            new RefreshRequest(tokens.RefreshToken, "some-completely-different-device-id"));
        mismatch.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var correctDeviceRetry = await _client.PostAsJsonAsync("/auth/refresh",
            new RefreshRequest(tokens.RefreshToken, DeviceId));
        correctDeviceRetry.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<TokenPairResponse> LoginAsOwnerAsync()
    {
        var response = await _client.PostAsJsonAsync("/auth/login",
            new LoginRequest(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword, DeviceId, "Owner Laptop", DevicePlatform.Web));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenPairResponse>())!;
    }
}
