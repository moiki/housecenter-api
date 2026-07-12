using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HouseCenter.Api.Domain.Entities;
using HouseCenter.Api.Features.Auth;

namespace HouseCenter.Api.Tests;

// HTTP-level sliding/absolute TTL scenarios (R6) — need to advance time by days, so each
// test builds its own dedicated `ApiFactory` instance (own `FakeTimeProvider`) instead of
// the shared `[Collection("Api")]` fixture (design.md "shared-fixture caution": a mutated
// clock on the shared instance would bleed into every other test in the collection). Still
// tagged `[Collection("Api")]` (without consuming the shared fixture) so xunit serializes
// these against the rest of the HTTP integration suite — a dedicated `ApiFactory` talks to
// the SAME real `housecenter_test` Postgres database and drops/recreates it on startup.
//
// Both tests drive the fake clock and then call POST /auth/refresh (the session path) —
// never a bearer-protected endpoint, since JwtBearer's access-token expiry uses its own
// internal clock and is intentionally not driven by FakeTimeProvider (design.md).
[Collection("Api")]
public sealed class SessionTtlTests
{
    private const string DeviceId = "session-ttl-tests-device";

    [Fact]
    public async Task Refresh_SlidingTtlExtendsOnUse()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var token = await LoginAsync(client);

        // Within the 60d sliding window, nowhere near the 180d absolute cap.
        factory.Time.Advance(TimeSpan.FromDays(30));

        var response = await client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(token, DeviceId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Refresh_AbsoluteCapNotExtendable_401AfterCap()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var token = await LoginAsync(client);

        // Three refreshes 50 days apart — each within the 60d sliding window, keeping the
        // session alive right up to (but never extending past) the 180d absolute cap.
        for (var i = 0; i < 3; i++)
        {
            factory.Time.Advance(TimeSpan.FromDays(50));
            var response = await client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(token, DeviceId));
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            token = (await response.Content.ReadFromJsonAsync<TokenPairResponse>())!.RefreshToken;
        }

        // Day 181 — past the 180d absolute cap set once at creation, never extended by any refresh.
        factory.Time.Advance(TimeSpan.FromDays(31));

        var expired = await client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(token, DeviceId));

        expired.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<string> LoginAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/auth/login",
            new LoginRequest(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword, DeviceId, null, DevicePlatform.Web));
        response.EnsureSuccessStatusCode();
        var tokens = await response.Content.ReadFromJsonAsync<TokenPairResponse>();
        return tokens!.RefreshToken;
    }
}
