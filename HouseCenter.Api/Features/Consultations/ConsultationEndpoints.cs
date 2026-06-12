using System.Security.Claims;
using HouseCenter.Api.Common.Pagination;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Common.Validation;
using HouseCenter.Api.Domain.Constants;
using HouseCenter.Api.Domain.Entities;
using MediatR;

namespace HouseCenter.Api.Features.Consultations;

public static class ConsultationEndpoints
{
    public static IEndpointRouteBuilder MapConsultationEndpoints(this IEndpointRouteBuilder app)
    {
        // StaffOnly at group level: sponsors get 403 on every consultation route
        var group = app.MapGroup("/consultations")
            .WithTags("MedicalConsultations")
            .RequireAuthorization(PolicyNames.StaffOnly);

        group.MapPost("/", async (CreateConsultationRequest request, ClaimsPrincipal principal,
                IMediator mediator, CancellationToken ct) =>
            {
                var result = await mediator.Send(new OpenConsultationCommand(
                    request.PatientId, request.TreatmentId, request.AssignedDoctorId,
                    request.Title, request.FirstMessage, request.AttachmentUrl, principal), ct);
                return result.Match(
                    created => Results.Created($"/consultations/{created.Id}", created),
                    error => error.ToProblem());
            })
            .RequireAuthorization(PolicyNames.MemberOrAbove)
            .WithValidation<CreateConsultationRequest>()
            .WithName("OpenConsultation");

        group.MapGet("/", async ([AsParameters] PageQuery paging, ConsultationStatus? status,
                ClaimsPrincipal principal, IMediator mediator, CancellationToken ct) =>
                (await mediator.Send(new ListConsultationsQuery(paging, status, principal), ct)).ToHttpResult())
            .WithName("ListConsultations");

        group.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal principal,
                IMediator mediator, CancellationToken ct) =>
                (await mediator.Send(new GetConsultationDetailQuery(id, principal), ct)).ToHttpResult())
            .WithName("GetConsultationDetail");

        group.MapPost("/{id:guid}/messages", async (Guid id, PostMessageRequest request,
                ClaimsPrincipal principal, IMediator mediator, CancellationToken ct) =>
            {
                var result = await mediator.Send(new PostConsultationMessageCommand(
                    id, request.Body, request.AttachmentUrl, principal), ct);
                return result.Match(
                    created => Results.Created($"/consultations/{id}/messages/{created.Id}", created),
                    error => error.ToProblem());
            })
            .WithValidation<PostMessageRequest>()
            .WithName("PostConsultationMessage");

        group.MapPatch("/{id:guid}/status", async (Guid id, UpdateConsultationStatusRequest request,
                ClaimsPrincipal principal, IMediator mediator, CancellationToken ct) =>
                (await mediator.Send(new UpdateConsultationStatusCommand(id, request.Status, principal), ct)).ToHttpResult())
            .WithValidation<UpdateConsultationStatusRequest>()
            .WithName("UpdateConsultationStatus");

        return app;
    }
}
