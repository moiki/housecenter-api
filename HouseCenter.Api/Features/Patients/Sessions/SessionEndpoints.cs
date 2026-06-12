using System.Security.Claims;
using HouseCenter.Api.Common.Pagination;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Common.Validation;
using HouseCenter.Api.Domain.Constants;
using HouseCenter.Api.Domain.Entities;
using MediatR;

namespace HouseCenter.Api.Features.Patients.Sessions;

public static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/patients/{patientId:guid}/sessions")
            .WithTags("AttentionSessions").RequireAuthorization();

        group.MapPost("/", async (Guid patientId, CreateAttentionSessionRequest request,
                IMediator mediator, CancellationToken ct) =>
            {
                var result = await mediator.Send(new CreateAttentionSessionCommand(
                    patientId, request.CollaboratorId, request.ClinicId, request.WorkRouteId,
                    request.AttentionType, request.SessionDate,
                    request.DurationMinutes, request.Notes), ct);
                return result.Match(
                    created => Results.Created($"/patients/{patientId}/sessions/{created.Id}", created),
                    error => error.ToProblem());
            })
            .RequireAuthorization(PolicyNames.MemberOrAbove)
            .WithValidation<CreateAttentionSessionRequest>()
            .WithName("CreateAttentionSession");

        group.MapGet("/", async (Guid patientId, [AsParameters] PageQuery paging,
                AttentionType? type, SessionStatus? status, DateTime? from, DateTime? to,
                IMediator mediator, CancellationToken ct) =>
                (await mediator.Send(new ListAttentionSessionsQuery(
                    patientId, paging, type, status, from, to), ct)).ToHttpResult())
            .RequireAuthorization(PolicyNames.StaffOnly)
            .WithName("ListAttentionSessions");

        group.MapPatch("/{sessionId:guid}/status", async (Guid patientId, Guid sessionId,
                UpdateSessionStatusRequest request, ClaimsPrincipal principal,
                IMediator mediator, CancellationToken ct) =>
                (await mediator.Send(new UpdateSessionStatusCommand(
                    patientId, sessionId, request.Status,
                    request.DurationMinutes, request.Notes, principal), ct)).ToHttpResult())
            .RequireAuthorization(PolicyNames.MemberOrAbove)
            .WithValidation<UpdateSessionStatusRequest>()
            .WithName("UpdateAttentionSessionStatus");

        group.MapDelete("/{sessionId:guid}", async (Guid patientId, Guid sessionId,
                IMediator mediator, CancellationToken ct) =>
                (await mediator.Send(new DeleteAttentionSessionCommand(patientId, sessionId), ct)).ToHttpResult())
            .RequireAuthorization(PolicyNames.AdministratorOrAbove)
            .WithName("DeleteAttentionSession");

        return app;
    }
}
