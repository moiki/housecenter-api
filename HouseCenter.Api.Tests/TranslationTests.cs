using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Constants;
using HouseCenter.Api.Domain.Entities;
using HouseCenter.Api.Features.Auth;
using HouseCenter.Api.Features.Translation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HouseCenter.Api.Tests;

[Collection("Api")]
public sealed class TranslationTests(ApiFactory factory)
{
    private const string Password = "Test123!pass";

    [Fact]
    public async Task Translate_AsMember_ReturnsTranslatedText()
    {
        var (client, _) = await LoginUserWithRoleAsync(RoleNames.Member);

        var response = await client.PostAsJsonAsync("/translate",
            new TranslateTextRequest("Hola, ¿cómo está el paciente?", Language.En, Language.Es));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TranslateTextResponse>();
        body!.TranslatedText.Should().Be("[En] Hola, ¿cómo está el paciente?");
        body.TargetLanguage.Should().Be("En");
        factory.Translation.Calls.Should().Contain(c =>
            c.Text == "Hola, ¿cómo está el paciente?" && c.TargetLanguage == Language.En);
    }

    [Fact]
    public async Task Translate_AsDoctor_ReturnsTranslatedText()
    {
        var (client, _) = await LoginUserWithRoleAsync(RoleNames.Doctor);

        var response = await client.PostAsJsonAsync("/translate",
            new TranslateTextRequest("The patient is improving.", Language.Es, Language.En));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Translate_AsSponsor_Returns403()
    {
        var (client, _) = await LoginUserWithRoleAsync(RoleNames.Sponsor);

        var response = await client.PostAsJsonAsync("/translate",
            new TranslateTextRequest("Anything", Language.En, Language.Es));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Translate_EmptyText_Returns400()
    {
        var (client, _) = await LoginUserWithRoleAsync(RoleNames.Member);

        var response = await client.PostAsJsonAsync("/translate",
            new TranslateTextRequest("", Language.En, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Translate_TextOverMaxLength_Returns400()
    {
        var (client, _) = await LoginUserWithRoleAsync(RoleNames.Member);

        var response = await client.PostAsJsonAsync("/translate",
            new TranslateTextRequest(new string('a', 5001), Language.En, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Translate_WithoutToken_Returns401()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/translate",
            new TranslateTextRequest("Hola", Language.En, null));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Translate_MoreThan20PerMinute_Returns429()
    {
        // Own dedicated user — the rate limit is keyed per-user, and this must not share a
        // counter with any other test's calls (e.g. the shared OwnerClient identity).
        var (client, _) = await LoginUserWithRoleAsync(RoleNames.Administrator);

        HttpResponseMessage? last = null;
        for (var i = 0; i < 21; i++)
        {
            last = await client.PostAsJsonAsync("/translate",
                new TranslateTextRequest($"Request {i}", Language.En, Language.Es));
        }

        var lastBody = await last!.Content.ReadAsStringAsync();
        last.StatusCode.Should().Be((HttpStatusCode)429, "response body was: {0}", lastBody);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private async Task<Guid> SeedUserAsync(string roleName, string? email = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HouseCenterDbContext>();
        var role = await db.Roles.SingleAsync(r => r.Name == roleName);

        var user = new User
        {
            FirstName = roleName,
            LastName = "Traduccion",
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

    private async Task<(HttpClient Client, Guid UserId)> LoginUserWithRoleAsync(string roleName)
    {
        var email = $"{roleName.ToLower()}-{Guid.NewGuid():N}@test.local";
        var userId = await SeedUserAsync(roleName, email);

        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/auth/login",
            new LoginRequest(email, Password, "test-device", null, DevicePlatform.Web));
        response.EnsureSuccessStatusCode();

        var tokens = await response.Content.ReadFromJsonAsync<TokenPairResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return (client, userId);
    }
}
