using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HouseCenter.Api.Common.Pagination;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Constants;
using HouseCenter.Api.Domain.Entities;
using HouseCenter.Api.Features.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
        var users = await response.Content.ReadFromJsonAsync<PagedResult<UserResponse>>();
        users!.Items.Should().NotBeEmpty();
        users.Items.Should().Contain(u => u.Email == ApiFactory.OwnerEmail);
    }

    [Fact]
    public async Task ListUsers_WithoutToken_Returns401()
    {
        var response = await factory.CreateClient().GetAsync("/users");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListUsers_DefaultPaging_ReturnsEnvelopeWithPageOneAndSizeTwenty()
    {
        var client = await Client();

        var response = await client.GetAsync("/users");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var paged = await response.Content.ReadFromJsonAsync<PagedResult<UserResponse>>();
        paged!.Page.Should().Be(1);
        paged.PageSize.Should().Be(20);
        paged.TotalCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ListUsers_Page2_ReturnsNextSlice()
    {
        var client = await Client();
        // Owner seeds one user; add more so page 2 with pageSize=1 has a distinct slice.
        for (var i = 0; i < 3; i++)
            await SeedMemberAsync($"paguser{i}-{Guid.NewGuid():N}@test.local");

        var page1 = await (await client.GetAsync("/users?page=1&pageSize=1"))
            .Content.ReadFromJsonAsync<PagedResult<UserResponse>>();
        var page2 = await (await client.GetAsync("/users?page=2&pageSize=1"))
            .Content.ReadFromJsonAsync<PagedResult<UserResponse>>();

        page1!.Items.Should().HaveCount(1);
        page2!.Items.Should().HaveCount(1);
        page2.Page.Should().Be(2);
        page1.Items[0].Id.Should().NotBe(page2.Items[0].Id);
    }

    [Fact]
    public async Task ListUsers_PageSizeAbove100_ClampsTo100()
    {
        var client = await Client();

        var response = await client.GetAsync("/users?page=1&pageSize=500");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var paged = await response.Content.ReadFromJsonAsync<PagedResult<UserResponse>>();
        paged!.PageSize.Should().Be(100);
    }

    [Fact]
    public async Task GetUser_OwnerById_ReturnsOwnerProfile()
    {
        var client = await Client();
        var users = await (await client.GetAsync("/users"))
            .Content.ReadFromJsonAsync<PagedResult<UserResponse>>();
        var owner = users!.Items.First(u => u.Email == ApiFactory.OwnerEmail);

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

    /// <summary>Inserts a Member-role user straight into the test DB (mirrors the
    /// SeedDoctorAsync helper in PatientFlowTests) — bypasses signup/email-confirmation flow.</summary>
    private async Task SeedMemberAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HouseCenterDbContext>();
        var role = await db.Roles.SingleAsync(r => r.Name == RoleNames.Member);

        var user = new User
        {
            FirstName = "Member",
            LastName = "Test",
            Email = email,
            HashedPassword = BCrypt.Net.BCrypt.HashPassword("Test123!pass"),
            Address = "Test 123",
            EmailConfirmed = true,
            Roles = [role]
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();
    }
}
