using FluentAssertions;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Entities;
using HouseCenter.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HouseCenter.Api.Tests;

// Unit tests for DatabaseSeeder — in-memory EF provider, no HTTP, no WebApplicationFactory.
// RecreateOnStartup/ApplyMigrationsAtStartup are both left false so StartAsync goes straight
// to the seed steps (the InMemory provider does not support real migrations).
public sealed class DatabaseSeederTests
{
    private static DatabaseSeeder CreateSeeder(IServiceProvider provider) =>
        new(
            provider,
            Microsoft.Extensions.Options.Options.Create(new SeedOptions()),
            Microsoft.Extensions.Options.Options.Create(new DatabaseOptions()),
            NullLogger<DatabaseSeeder>.Instance);

    [Fact]
    public async Task Seeder_RunsTwice_NoDuplicates_EditsSurviveReseed()
    {
        // The InMemory database name MUST be computed once and captured, not evaluated
        // inside the AddDbContext delegate — that delegate re-runs on every DbContext
        // instantiation (every scope), so Guid.NewGuid() there would hand each scope its
        // own isolated, empty database instead of sharing one across scopes.
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<HouseCenterDbContext>(o => o.UseInMemoryDatabase(dbName));
        var provider = services.BuildServiceProvider();

        var seeder = CreateSeeder(provider);

        // First run: seeds the 5 starter HelpTopics (insert-if-missing).
        await seeder.StartAsync(CancellationToken.None);

        const string editedSummary = "Editado por un administrador vía PUT — no debe perderse";
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HouseCenterDbContext>();
            var topics = await db.HelpTopics.ToListAsync();
            topics.Should().HaveCount(5);

            // Simulate an admin edit made through the API (PUT /help/topics/{id}).
            var edited = topics.Single(t => t.TopicKey == "sessions.recording");
            edited.Summary = editedSummary;
            await db.SaveChangesAsync();
        }

        // Second run: must not duplicate any row and must not overwrite the edit.
        await seeder.StartAsync(CancellationToken.None);

        await using var finalScope = provider.CreateAsyncScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<HouseCenterDbContext>();
        var finalTopics = await finalDb.HelpTopics.ToListAsync();

        finalTopics.Should().HaveCount(5);
        finalTopics.Select(t => t.TopicKey).Should().OnlyHaveUniqueItems();
        finalTopics.Single(t => t.TopicKey == "sessions.recording").Summary.Should().Be(editedSummary);
    }

    [Fact]
    public async Task Seeder_PartialSeed_BackfillsOnlyMissingTopics()
    {
        // Same isolation requirement as above — capture the InMemory database name once.
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<HouseCenterDbContext>(o => o.UseInMemoryDatabase(dbName));
        var provider = services.BuildServiceProvider();

        // Pre-seed 3 of the 5 starter topics BEFORE the seeder ever runs, simulating a
        // database that already has partial content (e.g. a prior manual insert or a
        // previous, interrupted seed run).
        const string preexistingSummary = "Contenido ya presente antes de correr el seeder";
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HouseCenterDbContext>();
            db.HelpTopics.AddRange(
                new HelpTopic
                {
                    TopicKey = "sessions.recording",
                    Title = "Existing",
                    Summary = preexistingSummary,
                    Body = "Existing body",
                    Audience = HelpAudience.Staff,
                    Category = "Existing",
                    DisplayOrder = 0
                },
                new HelpTopic
                {
                    TopicKey = "sessions.clinic-or-route",
                    Title = "Existing",
                    Summary = preexistingSummary,
                    Body = "Existing body",
                    Audience = HelpAudience.Staff,
                    Category = "Existing",
                    DisplayOrder = 0
                },
                new HelpTopic
                {
                    TopicKey = "patients.assign-doctor",
                    Title = "Existing",
                    Summary = preexistingSummary,
                    Body = "Existing body",
                    Audience = HelpAudience.Staff,
                    Category = "Existing",
                    DisplayOrder = 0
                });
            await db.SaveChangesAsync();
        }

        var seeder = CreateSeeder(provider);
        await seeder.StartAsync(CancellationToken.None);

        await using var finalScope = provider.CreateAsyncScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<HouseCenterDbContext>();
        var finalTopics = await finalDb.HelpTopics.ToListAsync();

        // All 5 starter topics now exist: the 3 pre-existing rows plus the 2 backfilled ones.
        finalTopics.Should().HaveCount(5);
        finalTopics.Select(t => t.TopicKey).Should().OnlyHaveUniqueItems();

        // The 2 missing topics were inserted by the seeder.
        finalTopics.Select(t => t.TopicKey).Should()
            .Contain(["sessions.status-lifecycle", "treatments.status-lifecycle"]);

        // The 3 pre-existing rows were left untouched — insert-if-missing never mutates them.
        foreach (var key in new[] { "sessions.recording", "sessions.clinic-or-route", "patients.assign-doctor" })
        {
            finalTopics.Single(t => t.TopicKey == key).Summary.Should().Be(preexistingSummary);
        }
    }
}
