using HouseCenter.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.Roles;

public sealed record RoleResponse(Guid Id, string Name);

public static class RoleEndpoints
{
    public static IEndpointRouteBuilder MapRoleEndpoints(this IEndpointRouteBuilder app)
    {
        // Read-only catalog endpoint — no service layer needed for a single trivial query
        app.MapGet("/roles", async (HouseCenterDbContext db, CancellationToken ct) =>
                Results.Ok(await db.Roles
                    .AsNoTracking()
                    .OrderBy(r => r.Name)
                    .Select(r => new RoleResponse(r.Id, r.Name))
                    .ToListAsync(ct)))
            .RequireAuthorization()
            .WithTags("Roles")
            .WithName("ListRoles");

        return app;
    }
}
