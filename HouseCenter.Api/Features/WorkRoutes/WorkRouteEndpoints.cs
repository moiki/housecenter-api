using HouseCenter.Api.Common.Pagination;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Common.Validation;
using HouseCenter.Api.Domain.Constants;

namespace HouseCenter.Api.Features.WorkRoutes;

public static class WorkRouteEndpoints
{
    public static IEndpointRouteBuilder MapWorkRouteEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/workroutes").WithTags("WorkRoutes").RequireAuthorization();

        group.MapGet("/", async ([AsParameters] PageQuery paging, IWorkRouteService service, CancellationToken ct) =>
                (await service.ListAsync(paging, ct)).ToHttpResult())
            .WithName("ListWorkRoutes");

        group.MapGet("/{id:guid}", async (Guid id, IWorkRouteService service, CancellationToken ct) =>
                (await service.GetByIdAsync(id, ct)).ToHttpResult())
            .WithName("GetWorkRoute");

        group.MapPost("/", async (CreateWorkRouteRequest request, IWorkRouteService service, CancellationToken ct) =>
            {
                var result = await service.CreateAsync(request, ct);
                return result.Match(
                    created => Results.Created($"/workroutes/{created.Id}", created),
                    error => error.ToProblem());
            })
            .RequireAuthorization(PolicyNames.AdministratorOrAbove)
            .WithValidation<CreateWorkRouteRequest>()
            .WithName("CreateWorkRoute");

        group.MapPut("/{id:guid}", async (Guid id, UpdateWorkRouteRequest request, IWorkRouteService service, CancellationToken ct) =>
                (await service.UpdateAsync(id, request, ct)).ToHttpResult())
            .RequireAuthorization(PolicyNames.AdministratorOrAbove)
            .WithValidation<UpdateWorkRouteRequest>()
            .WithName("UpdateWorkRoute");

        group.MapDelete("/{id:guid}", async (Guid id, IWorkRouteService service, CancellationToken ct) =>
                (await service.DeactivateAsync(id, ct)).ToHttpResult())
            .RequireAuthorization(PolicyNames.AdministratorOrAbove)
            .WithName("DeactivateWorkRoute");

        return app;
    }
}
