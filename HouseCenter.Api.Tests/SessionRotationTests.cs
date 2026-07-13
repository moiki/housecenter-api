using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HouseCenter.Api.Domain.Entities;
using HouseCenter.Api.Features.Auth;
using HouseCenter.Api.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HouseCenter.Api.Tests;

// HTTP-level grace-window-boundary and reuse-after-window scenarios (R4, R5) — the two
// rotation scenarios that need to move time PAST the grace deadline. Each test builds its
// own dedicated `ApiFactory` instance (own `FakeTimeProvider`) rather than the shared
// `[Collection("Api")]` fixture: advancing the shared fixture's clock would bleed into
// every other test in the collection (design.md "shared-fixture caution"). Still tagged
// `[Collection("Api")]` (without consuming the shared fixture) purely so xunit runs these
// sequentially with the rest of the HTTP integration suite — a dedicated `ApiFactory` talks
// to the SAME real `housecenter_test` Postgres database and drops/recreates it on startup,
// which must never race with another instance's in-flight requests.
//
// Basic rotation (login -> refresh -> new token != old), the DB-level hash-shift proof,
// grace-window accept, and deviceId mismatch don't need time control and live in
// LoginFlowTests.cs instead (no dedicated fixture needed there).
[Collection("Api")]
public sealed class SessionRotationTests
{
    private const string DeviceId = "session-rotation-tests-device";

    [Fact]
    public async Task Refresh_OneSecondPastGraceDeadline_Returns401()
    {
        await using var factory = new ApiFactory();
        var graceWindowSeconds = factory.Services
            .GetRequiredService<IOptions<UserSessionOptions>>().Value.RefreshGraceWindowSeconds;
        var client = factory.CreateClient();

        var t1 = await LoginAsync(client);
        var rotateResponse = await client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(t1, DeviceId));
        rotateResponse.EnsureSuccessStatusCode();

        // t1 is now Previous; step one second past its grace deadline.
        factory.Time.Advance(TimeSpan.FromSeconds(graceWindowSeconds + 1));

        var replay = await client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(t1, DeviceId));

        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_ReuseAfterGrace_RevokesSessionAnd401sBoth()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var t1 = await LoginAsync(client);
        var rotateResponse = await client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(t1, DeviceId));
        rotateResponse.EnsureSuccessStatusCode();
        var t2 = (await rotateResponse.Content.ReadFromJsonAsync<TokenPairResponse>())!.RefreshToken;

        // Far past the grace deadline — a stolen-token replay, not an off-by-one edge case.
        factory.Time.Advance(TimeSpan.FromHours(1));

        var replayWithT1 = await client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(t1, DeviceId));
        replayWithT1.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // R5: revocation invalidates the WHOLE session immediately — T2 (still "Current"
        // right up until this replay was detected) must now also fail.
        var retryWithT2 = await client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(t2, DeviceId));
        retryWithT2.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
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
