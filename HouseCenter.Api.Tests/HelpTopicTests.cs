using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Constants;
using HouseCenter.Api.Domain.Entities;
using HouseCenter.Api.Features.Auth;
using HouseCenter.Api.Features.Help;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HouseCenter.Api.Tests;

[Collection("Api")]
public sealed class HelpTopicTests(ApiFactory factory)
{
    private const string Password = "Test123!pass";

    [Fact]
    public async Task ListTopics_Unauthenticated_Returns401()
    {
        var response = await factory.CreateClient().GetAsync("/help/topics");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListTopics_AsSponsor_ReturnsOnlyAllAudience()
    {
        var allKey = await SeedTopicAsync(HelpAudience.All);
        var staffKey = await SeedTopicAsync(HelpAudience.Staff);
        var (sponsorClient, _) = await LoginUserWithRoleAsync(RoleNames.Sponsor);

        var response = await sponsorClient.GetAsync("/help/topics");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var topics = (await response.Content.ReadFromJsonAsync<List<HelpTopicSummaryResponse>>())!;
        topics.Select(t => t.TopicKey).Should().Contain(allKey);
        topics.Select(t => t.TopicKey).Should().NotContain(staffKey);
    }

    [Fact]
    public async Task ListTopics_AsMember_ExcludesAdministratorOrAboveTopics()
    {
        var memberKey = await SeedTopicAsync(HelpAudience.MemberOrAbove);
        var adminKey = await SeedTopicAsync(HelpAudience.AdministratorOrAbove);
        var (memberClient, _) = await LoginUserWithRoleAsync(RoleNames.Member);

        var response = await memberClient.GetAsync("/help/topics");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var topics = (await response.Content.ReadFromJsonAsync<List<HelpTopicSummaryResponse>>())!;
        topics.Select(t => t.TopicKey).Should().Contain(memberKey);
        topics.Select(t => t.TopicKey).Should().NotContain(adminKey);
    }

    [Fact]
    public async Task GetTopic_OutsideAudience_Returns404()
    {
        var staffKey = await SeedTopicAsync(HelpAudience.Staff);
        var (sponsorClient, _) = await LoginUserWithRoleAsync(RoleNames.Sponsor);

        var response = await sponsorClient.GetAsync($"/help/topics/{staffKey}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetTopic_WithinAudience_Returns200WithBody()
    {
        var allKey = await SeedTopicAsync(HelpAudience.All);
        var (sponsorClient, _) = await LoginUserWithRoleAsync(RoleNames.Sponsor);

        var response = await sponsorClient.GetAsync($"/help/topics/{allKey}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var topic = await response.Content.ReadFromJsonAsync<HelpTopicResponse>();
        topic.Should().NotBeNull();
        topic!.TopicKey.Should().Be(allKey);
        topic.Body.Should().Be("Test body");
    }

    [Fact]
    public async Task CreateTopic_AsMember_Returns403()
    {
        var (memberClient, _) = await LoginUserWithRoleAsync(RoleNames.Member);
        var request = ValidCreateRequest();

        var response = await memberClient.PostAsJsonAsync("/help/topics", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateTopic_DuplicateActiveKey_Returns409()
    {
        var activeKey = await SeedTopicAsync(HelpAudience.All);
        var (adminClient, _) = await LoginUserWithRoleAsync(RoleNames.Administrator);
        var request = ValidCreateRequest() with { TopicKey = activeKey };

        var response = await adminClient.PostAsJsonAsync("/help/topics", request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateTopic_DuplicateSoftDeletedKey_Returns409()
    {
        var softDeletedKey = await SeedSoftDeletedTopicAsync();
        var (adminClient, _) = await LoginUserWithRoleAsync(RoleNames.Administrator);
        var request = ValidCreateRequest() with { TopicKey = softDeletedKey };

        var response = await adminClient.PostAsJsonAsync("/help/topics", request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateTopic_InvalidKeyFormat_Returns400()
    {
        var (adminClient, _) = await LoginUserWithRoleAsync(RoleNames.Administrator);
        var request = ValidCreateRequest() with { TopicKey = "Not A Valid Key!" };

        var response = await adminClient.PostAsJsonAsync("/help/topics", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private async Task<string> SeedTopicAsync(HelpAudience audience)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HouseCenterDbContext>();

        var topicKey = $"test.topic-{Guid.NewGuid():N}";
        db.HelpTopics.Add(new HelpTopic
        {
            TopicKey = topicKey,
            Title = "Test Topic",
            Summary = "Test summary",
            Body = "Test body",
            Audience = audience,
            Category = "Test",
            DisplayOrder = 0
        });
        await db.SaveChangesAsync();
        return topicKey;
    }

    private async Task<string> SeedSoftDeletedTopicAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HouseCenterDbContext>();

        var topicKey = $"test.topic-{Guid.NewGuid():N}";
        db.HelpTopics.Add(new HelpTopic
        {
            TopicKey = topicKey,
            Title = "Test Topic",
            Summary = "Test summary",
            Body = "Test body",
            Audience = HelpAudience.All,
            Category = "Test",
            DisplayOrder = 0,
            IsActive = false
        });
        await db.SaveChangesAsync();
        return topicKey;
    }

    private static CreateHelpTopicRequest ValidCreateRequest() => new(
        TopicKey: $"test.topic-{Guid.NewGuid():N}",
        Title: "Test Topic",
        Summary: "Test summary",
        Body: "Test body",
        Audience: HelpAudience.All,
        Category: "Test",
        DisplayOrder: 0);

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

    private async Task<Guid> SeedUserAsync(string roleName, string? email = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HouseCenterDbContext>();
        var role = await db.Roles.SingleAsync(r => r.Name == roleName);

        var user = new User
        {
            FirstName = roleName,
            LastName = "Ayuda",
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
}
