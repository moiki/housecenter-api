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
        // R5 requires the 409 to distinguish "exists but unpublished" from a plain active
        // duplicate — assert the actual message text (CreateHelpTopic.cs), not just the code.
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("A help topic with this key exists but is unpublished");
    }

    [Fact]
    public async Task CreateTopic_AsAdmin_Returns201WithBody()
    {
        var (adminClient, _) = await LoginUserWithRoleAsync(RoleNames.Administrator);
        var request = ValidCreateRequest();

        var response = await adminClient.PostAsJsonAsync("/help/topics", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        var topic = await response.Content.ReadFromJsonAsync<HelpTopicResponse>();
        topic.Should().NotBeNull();
        topic!.TopicKey.Should().Be(request.TopicKey);
        topic.Title.Should().Be(request.Title);
        topic.Summary.Should().Be(request.Summary);
        topic.Body.Should().Be(request.Body);
        topic.Audience.Should().Be(nameof(HelpAudience.All));
        topic.Category.Should().Be(request.Category);
        topic.DisplayOrder.Should().Be(request.DisplayOrder);
    }

    [Fact]
    public async Task CreateTopic_InvalidKeyFormat_Returns400()
    {
        var (adminClient, _) = await LoginUserWithRoleAsync(RoleNames.Administrator);
        var request = ValidCreateRequest() with { TopicKey = "Not A Valid Key!" };

        var response = await adminClient.PostAsJsonAsync("/help/topics", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateTopic_ChangesFields_PersistsCorrectly()
    {
        var (id, topicKey) = await SeedTopicWithIdAsync(HelpAudience.All);
        var (adminClient, _) = await LoginUserWithRoleAsync(RoleNames.Administrator);
        var request = new UpdateHelpTopicRequest(
            Title: "Updated Title",
            Summary: "Updated summary",
            Body: "Updated body",
            Audience: HelpAudience.Staff,
            Category: "Updated",
            DisplayOrder: 5);

        var response = await adminClient.PutAsJsonAsync($"/help/topics/{id}", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var topic = await response.Content.ReadFromJsonAsync<HelpTopicResponse>();
        topic.Should().NotBeNull();
        // TopicKey is unchanged — the request DTO carries no TopicKey field, so there is
        // structurally nothing that could have renamed it (R1 immutability).
        topic!.TopicKey.Should().Be(topicKey);
        topic.Title.Should().Be("Updated Title");
        topic.Summary.Should().Be("Updated summary");
        topic.Body.Should().Be("Updated body");
        topic.Audience.Should().Be(nameof(HelpAudience.Staff));
        topic.Category.Should().Be("Updated");
        topic.DisplayOrder.Should().Be(5);
    }

    [Fact]
    public async Task UpdateTopic_AsMember_Returns403()
    {
        var (id, _) = await SeedTopicWithIdAsync(HelpAudience.All);
        var (memberClient, _) = await LoginUserWithRoleAsync(RoleNames.Member);
        var request = new UpdateHelpTopicRequest(
            Title: "Updated Title",
            Summary: "Updated summary",
            Body: "Updated body",
            Audience: HelpAudience.Staff,
            Category: "Updated",
            DisplayOrder: 5);

        var response = await memberClient.PutAsJsonAsync($"/help/topics/{id}", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateTopic_NotFound_Returns404()
    {
        var (adminClient, _) = await LoginUserWithRoleAsync(RoleNames.Administrator);
        var request = new UpdateHelpTopicRequest(
            Title: "Title",
            Summary: "Summary",
            Body: "Body",
            Audience: HelpAudience.All,
            Category: "Category",
            DisplayOrder: 0);

        var response = await adminClient.PutAsJsonAsync($"/help/topics/{Guid.NewGuid()}", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteTopic_AsMember_Returns403()
    {
        var (id, _) = await SeedTopicWithIdAsync(HelpAudience.All);
        var (memberClient, _) = await LoginUserWithRoleAsync(RoleNames.Member);

        var response = await memberClient.DeleteAsync($"/help/topics/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteTopic_NotFound_Returns404()
    {
        var (adminClient, _) = await LoginUserWithRoleAsync(RoleNames.Administrator);

        var response = await adminClient.DeleteAsync($"/help/topics/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteTopic_ThenGet_Returns404ForEveryRole()
    {
        var (id, topicKey) = await SeedTopicWithIdAsync(HelpAudience.All);
        var (adminClient, _) = await LoginUserWithRoleAsync(RoleNames.Administrator);

        var deleteResponse = await adminClient.DeleteAsync($"/help/topics/{id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        foreach (var role in RoleNames.All)
        {
            var (roleClient, _) = await LoginUserWithRoleAsync(role);
            var getResponse = await roleClient.GetAsync($"/help/topics/{topicKey}");
            getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
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

    private async Task<(Guid Id, string TopicKey)> SeedTopicWithIdAsync(HelpAudience audience)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HouseCenterDbContext>();

        var topicKey = $"test.topic-{Guid.NewGuid():N}";
        var topic = new HelpTopic
        {
            TopicKey = topicKey,
            Title = "Test Topic",
            Summary = "Test summary",
            Body = "Test body",
            Audience = audience,
            Category = "Test",
            DisplayOrder = 0
        };
        db.HelpTopics.Add(topic);
        await db.SaveChangesAsync();
        return (topic.Id, topicKey);
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
