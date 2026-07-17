using System.Net.Http.Headers;
using System.Net.Http.Json;
using HouseCenter.Api.Common.Email;
using HouseCenter.Api.Common.Push;
using HouseCenter.Api.Common.Translation;
using HouseCenter.Api.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
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

    /// <summary>Default deviceId used by <see cref="CreateAuthenticatedClientAsync"/> — tests
    /// that need per-device isolation (e.g. session rotation/reuse scenarios) should log in
    /// directly against a distinct deviceId instead of relying on this shared one.</summary>
    public const string DefaultDeviceId = "test-owner-device";

    /// <summary>Captures notification/invitation emails instead of sending them, so tests
    /// can assert delivery. Shared across the collection — filter by recipient address.</summary>
    public RecordingEmailService Emails { get; } = new();

    /// <summary>Captures push sends instead of hitting FCM; supports invalid-token simulation.</summary>
    public RecordingPushSender Push { get; } = new();

    /// <summary>Captures translation calls instead of hitting Google Cloud Translation.</summary>
    public RecordingTranslationClient Translation { get; } = new();

    /// <summary>Fake clock driving ONLY <see cref="UserSession"/> TTL logic (design.md
    /// TimeProvider integration) — never JwtBearer's internal access-token clock, so
    /// advancing it has no effect on bearer-protected endpoints; time-driven refresh
    /// scenarios must go through <c>/auth/refresh</c>. Starts at the real current instant
    /// and stays frozen unless a test calls <see cref="FakeTimeProvider.Advance(TimeSpan)"/>
    /// — harmless for the shared <c>[Collection("Api")]</c> fixture as long as no
    /// shared-fixture test advances it. Grace-window-boundary and TTL scenarios MUST
    /// construct their own dedicated <c>new ApiFactory()</c> instance instead of reusing
    /// the shared collection fixture, so their clock manipulation never bleeds into every
    /// other test sharing this fixture (design.md "shared-fixture caution").</summary>
    public FakeTimeProvider Time { get; } = new(DateTimeOffset.UtcNow);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailService>();
            services.AddSingleton<IEmailService>(Emails);
            services.RemoveAll<IPushSender>();
            services.AddSingleton<IPushSender>(Push);
            services.RemoveAll<ITranslationClient>();
            services.AddSingleton<ITranslationClient>(Translation);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
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
                ["Jwt:InvitationSecret"] = "integration-test-invite-secret-0003",
                ["Jwt:ResetSecret"] = "integration-test-reset-secret-0004",
                ["Email:Host"] = "", // emails are logged, never sent
                ["Seed:OwnerEmail"] = OwnerEmail,
                ["Seed:OwnerPassword"] = OwnerPassword,
                ["App:FrontendUrl"] = "http://localhost:3000",
                // The reminder scanner is driven directly in tests; keep the timed service off
                ["SessionReminders:Enabled"] = "false",
                // Attachments: store under a temp dir; small cap so the oversize test is cheap
                ["FileStorage:LocalRootPath"] = Path.Combine(Path.GetTempPath(), "housecenter-test-attachments"),
                ["FileStorage:MaxFileSizeBytes"] = "65536",
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
            new LoginRequest(OwnerEmail, OwnerPassword, DefaultDeviceId, "Test Owner Device", DevicePlatform.Web));
        response.EnsureSuccessStatusCode();

        var tokens = await response.Content.ReadFromJsonAsync<TokenPairResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        return client;
    }
}

[CollectionDefinition("Api")]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>;
