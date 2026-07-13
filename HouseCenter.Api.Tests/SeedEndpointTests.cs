using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using HouseCenter.Api.Domain.Entities;
using HouseCenter.Api.Features.Auth;
using HouseCenter.Api.Features.Seeding;

namespace HouseCenter.Api.Tests;

[Collection("Api")]
public sealed class SeedEndpointTests(ApiFactory factory)
{
    // Default of SeedOptions.TestUserPassword — ApiFactory does not override it
    private const string TestUserPassword = "Demo@123!";

    [Fact]
    public async Task PostSeed_WithoutToken_Returns401()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsync("/seed", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostSeed_AsOwner_ReportsTheFullDataSet()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync("/seed", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<SeedResponse>();
        // Created/Skipped split depends on whether another test seeded first — the sum doesn't
        (result!.Users.Created + result.Users.Skipped).Should().Be(5);
        (result.Clinics.Created + result.Clinics.Skipped).Should().Be(3);
        (result.Collaborators.Created + result.Collaborators.Skipped).Should().Be(3);
        (result.Patients.Created + result.Patients.Skipped).Should().Be(6);
    }

    [Fact]
    public async Task PostSeed_Twice_IsIdempotent()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        await client.PostAsync("/seed", null);

        var second = await client.PostAsync("/seed", null);

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await second.Content.ReadFromJsonAsync<SeedResponse>();
        result!.Users.Created.Should().Be(0);
        result.Clinics.Created.Should().Be(0);
        result.Collaborators.Created.Should().Be(0);
        result.Patients.Created.Should().Be(0);
    }

    [Fact]
    public async Task SeededMember_CanLogin_ButCannotSeed()
    {
        var owner = await factory.CreateAuthenticatedClientAsync();
        await owner.PostAsync("/seed", null);

        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/auth/login",
            new LoginRequest("member@housecenter.dev", TestUserPassword, "test-device", null, DevicePlatform.Web));
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        var tokens = await login.Content.ReadFromJsonAsync<TokenPairResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        var seedAttempt = await client.PostAsync("/seed", null);
        seedAttempt.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
