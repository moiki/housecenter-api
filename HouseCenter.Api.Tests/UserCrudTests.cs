using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HouseCenter.Api.Features.Users;

namespace HouseCenter.Api.Tests;

[Collection("Api")]
public sealed class UserCrudTests(ApiFactory factory)
{
    private Task<HttpClient> Client() => factory.CreateAuthenticatedClientAsync();

    [Fact]
    public async Task ListUsers_AsOwner_ReturnsAtLeastTheOwner()
    {
        var client = await Client();

        var response = await client.GetAsync("/users");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var users = await response.Content.ReadFromJsonAsync<List<UserResponse>>();
        users.Should().NotBeEmpty();
        users!.Should().Contain(u => u.Email == ApiFactory.OwnerEmail);
    }

    [Fact]
    public async Task ListUsers_WithoutToken_Returns401()
    {
        var response = await factory.CreateClient().GetAsync("/users");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetUser_OwnerById_ReturnsOwnerProfile()
    {
        var client = await Client();
        var users = await (await client.GetAsync("/users"))
            .Content.ReadFromJsonAsync<List<UserResponse>>();
        var owner = users!.First(u => u.Email == ApiFactory.OwnerEmail);

        var response = await client.GetAsync($"/users/{owner.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var user = await response.Content.ReadFromJsonAsync<UserResponse>();
        user!.Email.Should().Be(ApiFactory.OwnerEmail);
        user.Roles.Should().Contain("Owner");
    }

    [Fact]
    public async Task GetUser_WithUnknownId_Returns404()
    {
        var client = await Client();

        var response = await client.GetAsync($"/users/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateTheme_SwitchesToDarkMode_Returns204()
    {
        var client = await Client();

        var response = await client.PatchAsJsonAsync("/users/me/theme",
            new UpdateThemeRequest(DarkMode: true));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task UpdateTheme_WithoutToken_Returns401()
    {
        var response = await factory.CreateClient().PatchAsJsonAsync("/users/me/theme",
            new UpdateThemeRequest(DarkMode: true));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
