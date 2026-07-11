using System.Security.Claims;
using HouseCenter.Api.Common.Pagination;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Common.Validation;
using HouseCenter.Api.Domain.Constants;

namespace HouseCenter.Api.Features.Users;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        // Group-level authorization: every endpoint here requires at least a valid token
        var group = app.MapGroup("/users").WithTags("Users").RequireAuthorization();

        group.MapGet("/", async ([AsParameters] PageQuery paging, IUserService usersService, CancellationToken ct) =>
                (await usersService.ListAsync(paging, ct)).ToHttpResult())
            .WithName("ListUsers");

        group.MapGet("/{id:guid}", async (Guid id, IUserService usersService, CancellationToken ct) =>
                (await usersService.GetByIdAsync(id, ct)).ToHttpResult())
            .WithName("GetUser");

        group.MapPut("/{id:guid}", async (Guid id, UpdateUserRequest request, IUserService usersService, CancellationToken ct) =>
                (await usersService.UpdateAsync(id, request, ct)).ToHttpResult())
            .RequireAuthorization(PolicyNames.AdministratorOrAbove)
            .WithValidation<UpdateUserRequest>()
            .WithName("UpdateUser");

        group.MapDelete("/{id:guid}", async (Guid id, IUserService usersService, CancellationToken ct) =>
                (await usersService.DeactivateAsync(id, ct)).ToHttpResult())
            .RequireAuthorization(PolicyNames.AdministratorOrAbove)
            .WithName("DeactivateUser");

        // Role assignment can escalate privileges → Owner only
        group.MapPut("/{id:guid}/roles", async (Guid id, AssignRolesRequest request, IUserService usersService, CancellationToken ct) =>
                (await usersService.AssignRolesAsync(id, request, ct)).ToHttpResult())
            .RequireAuthorization(PolicyNames.OwnerOnly)
            .WithValidation<AssignRolesRequest>()
            .WithName("AssignUserRoles");

        // Self-service: the user id comes from the TOKEN, never from the route
        group.MapPatch("/me/theme", async (ClaimsPrincipal principal, UpdateThemeRequest request, IUserService usersService, CancellationToken ct) =>
            {
                var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
                return (await usersService.UpdateThemeAsync(userId, request, ct)).ToHttpResult();
            })
            .WithName("UpdateMyTheme");

        return app;
    }
}
