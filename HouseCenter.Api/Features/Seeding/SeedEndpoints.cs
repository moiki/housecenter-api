using HouseCenter.Api.Domain.Constants;

namespace HouseCenter.Api.Features.Seeding;

public static class SeedEndpoints
{
    public static IEndpointRouteBuilder MapSeedEndpoints(this IEndpointRouteBuilder app)
    {
        // Test data is created only on explicit request — never at startup. Program.cs
        // maps this group in Development/Staging only, so in Production the route 404s.
        app.MapPost("/seed", async (TestDataSeeder seeder, CancellationToken ct) =>
                Results.Ok(await seeder.SeedAsync(ct)))
            .RequireAuthorization(PolicyNames.AdministratorOrAbove)
            .WithTags("Seeding")
            .WithName("SeedTestData");

        return app;
    }
}
