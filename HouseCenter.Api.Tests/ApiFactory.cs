using System.Net.Http.Headers;
using System.Net.Http.Json;
using HouseCenter.Api.Common.Email;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using HouseCenter.Api.Features.Auth;
using Npgsql;

namespace HouseCenter.Api.Tests;

/// <summary>
/// Boots the real app in-memory against a dedicated `housecenter_test` database.
/// Reuses the developer's local connection string (user secrets) but swaps the DB name,
/// and recreates the schema from migrations on startup.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string OwnerEmail = "owner@test.local";
    public const string OwnerPassword = "Owner123!test";

    /// <summary>Captures notification/invitation emails instead of sending them, so tests
    /// can assert delivery. Shared across the collection — filter by recipient address.</summary>
    public RecordingEmailService Emails { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailService>();
            services.AddSingleton<IEmailService>(Emails);
        });

        builder.ConfigureAppConfiguration((_, config) =>
        {
            // Build what's registered so far (includes user secrets) to read the dev connection string
            var current = config.Build();
            var devConnectionString = current["Database:ConnectionString"]
                ?? throw new InvalidOperationException(
                    "Database:ConnectionString not found — configure user secrets before running integration tests");

            var testConnectionString = new NpgsqlConnectionStringBuilder(devConnectionString)
            {
                Database = "housecenter_test"
            }.ConnectionString;

            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = testConnectionString,
                ["Database:RecreateOnStartup"] = "true",
                ["Jwt:Secret"] = "integration-test-access-secret-0001",
                ["Jwt:RefreshSecret"] = "integration-test-refresh-secret-0002",
                ["Jwt:InvitationSecret"] = "integration-test-invite-secret-0003",
                ["Jwt:ResetSecret"] = "integration-test-reset-secret-0004",
                ["Email:Host"] = "", // emails are logged, never sent
                ["Seed:OwnerEmail"] = OwnerEmail,
                ["Seed:OwnerPassword"] = OwnerPassword,
                ["App:FrontendUrl"] = "http://localhost:3000",
                // Surface the JwtBearer rejection reason in test output
                ["Serilog:MinimumLevel:Override:Microsoft.AspNetCore.Authentication"] = "Debug"
            });
        });
    }

    /// <summary>
    /// Returns an HttpClient with the Authorization header pre-set to an Owner access token.
    /// Call once per test — each call performs a real login against the test DB.
    /// </summary>
    public async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/auth/login",
            new LoginRequest(OwnerEmail, OwnerPassword));
        response.EnsureSuccessStatusCode();

        var tokens = await response.Content.ReadFromJsonAsync<TokenPairResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        return client;
    }
}

[CollectionDefinition("Api")]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>;
