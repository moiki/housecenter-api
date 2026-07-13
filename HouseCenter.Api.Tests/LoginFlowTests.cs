using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using HouseCenter.Api.Domain.Entities;
using HouseCenter.Api.Features.Auth;

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

    private async Task<TokenPairResponse> LoginAsOwnerAsync()
    {
        var response = await _client.PostAsJsonAsync("/auth/login",
            new LoginRequest(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword, DeviceId, "Owner Laptop", DevicePlatform.Web));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenPairResponse>())!;
    }
}
