using HouseCenter.Api.Domain.Constants;
using HouseCenter.Api.Domain.Entities;
using HouseCenter.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HouseCenter.Api.Data;

public class DatabaseSeeder(
    IServiceProvider serviceProvider,
    IOptions<SeedOptions> seedOptions,
    IOptions<DatabaseOptions> databaseOptions,
    ILogger<DatabaseSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // DbContext is Scoped; a hosted service is Singleton — create a scope manually
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HouseCenterDbContext>();

        if (databaseOptions.Value.RecreateOnStartup)
        {
            logger.LogWarning("RecreateOnStartup is ON — dropping database (tests only!)");
            await db.Database.EnsureDeletedAsync(cancellationToken);
            await db.Database.MigrateAsync(cancellationToken);
        }
        else if (databaseOptions.Value.ApplyMigrationsAtStartup)
        {
            await db.Database.MigrateAsync(cancellationToken);
        }

        await SeedRolesAsync(db, cancellationToken);
        await SeedOwnerUserAsync(db, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SeedRolesAsync(HouseCenterDbContext db, CancellationToken ct)
    {
        var existing = await db.Roles
            .Select(r => r.Name)
            .ToListAsync(ct);

        var missing = RoleNames.All.Except(existing).ToList();
        if (missing.Count == 0) return;

        db.Roles.AddRange(missing.Select(name => new Role { Name = name }));
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Seeded roles: {Roles}", string.Join(", ", missing));
    }

    private async Task SeedOwnerUserAsync(HouseCenterDbContext db, CancellationToken ct)
    {
        var seed = seedOptions.Value;

        if (string.IsNullOrWhiteSpace(seed.OwnerEmail) || string.IsNullOrWhiteSpace(seed.OwnerPassword))
        {
            logger.LogWarning("Seed:OwnerEmail / Seed:OwnerPassword not configured — skipping owner seed");
            return;
        }

        var exists = await db.Users.AnyAsync(u => u.Email == seed.OwnerEmail, ct);
        if (exists) return;

        var ownerRole = await db.Roles.SingleAsync(r => r.Name == RoleNames.Owner, ct);

        db.Users.Add(new User
        {
            FirstName = seed.OwnerFirstName,
            LastName = seed.OwnerLastName,
            Email = seed.OwnerEmail,
            HashedPassword = BCrypt.Net.BCrypt.HashPassword(seed.OwnerPassword),
            Address = "N/A",
            EmailConfirmed = true,
            Roles = [ownerRole]
        });
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Seeded owner user {Email}", seed.OwnerEmail);
    }
}
