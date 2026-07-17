using System.Security.Claims;
using HouseCenter.Api.Common.Pagination;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Common.Validation;
using HouseCenter.Api.Domain.Constants;

namespace HouseCenter.Api.Features.Collaborators;

public static class CollaboratorEndpoints
{
    public static IEndpointRouteBuilder MapCollaboratorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/collaborators").WithTags("Collaborators").RequireAuthorization();

        group.MapGet("/", async ([AsParameters] PageQuery paging, ICollaboratorService service, CancellationToken ct) =>
                (await service.ListAsync(paging, ct)).ToHttpResult())
            .WithName("ListCollaborators");

        // Registered BEFORE "/{id:guid}" so "me" is never captured as an id — belt-and-suspenders
        // with the :guid route constraint, which already makes "me" fail to match that route.
        // No extra .RequireAuthorization(...) — inherits the group's bare RequireAuthorization()
        // (any authenticated role, matching ListAsync/GetByIdAsync).
        group.MapGet("/me", async (ClaimsPrincipal principal, ICollaboratorService service, CancellationToken ct) =>
                (await service.GetMeAsync(principal, ct)).ToHttpResult())
            .WithName("GetMyCollaboratorProfile");

        group.MapGet("/{id:guid}", async (Guid id, ICollaboratorService service, CancellationToken ct) =>
                (await service.GetByIdAsync(id, ct)).ToHttpResult())
            .WithName("GetCollaborator");

        group.MapPost("/", async (CreateCollaboratorRequest request, ICollaboratorService service, CancellationToken ct) =>
            {
                var result = await service.CreateAsync(request, ct);
                return result.Match(
                    created => Results.Created($"/collaborators/{created.Id}", created),
                    error => error.ToProblem());
            })
            .RequireAuthorization(PolicyNames.AdministratorOrAbove)
            .WithValidation<CreateCollaboratorRequest>()
            .WithName("CreateCollaborator");

        // Maintenance endpoint (Phase 3, reconciliation) — deliberately NOT Dev/Staging-gated
        // like POST /seed: /seed only inserts fake test data, but this backfills real prod
        // data, so gating it to non-prod would defeat its purpose. Idempotent; safe to
        // re-run. See tasks.md "Decisions Resolved" #2 and design.md Decision 2.
        group.MapPost("/reconcile", async (ICollaboratorReconciliationService service, CancellationToken ct) =>
                (await service.ReconcileAsync(ct)).ToHttpResult())
            .RequireAuthorization(PolicyNames.AdministratorOrAbove)
            .WithName("ReconcileCollaborators");

        group.MapPut("/{id:guid}", async (Guid id, UpdateCollaboratorRequest request, ICollaboratorService service, CancellationToken ct) =>
                (await service.UpdateAsync(id, request, ct)).ToHttpResult())
            .RequireAuthorization(PolicyNames.AdministratorOrAbove)
            .WithValidation<UpdateCollaboratorRequest>()
            .WithName("UpdateCollaborator");

        group.MapDelete("/{id:guid}", async (Guid id, ICollaboratorService service, CancellationToken ct) =>
                (await service.DeactivateAsync(id, ct)).ToHttpResult())
            .RequireAuthorization(PolicyNames.AdministratorOrAbove)
            .WithName("DeactivateCollaborator");

        return app;
    }
}
