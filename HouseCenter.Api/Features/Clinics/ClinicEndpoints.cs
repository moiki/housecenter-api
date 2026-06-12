using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Common.Validation;
using HouseCenter.Api.Domain.Constants;
using HouseCenter.Api.Features.Clinics.Commands;
using HouseCenter.Api.Features.Clinics.Queries;
using MediatR;

namespace HouseCenter.Api.Features.Clinics;

public static class ClinicEndpoints
{
    public static IEndpointRouteBuilder MapClinicEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/clinics").WithTags("Clinics").RequireAuthorization();

        group.MapGet("/", async (IMediator mediator, CancellationToken ct) =>
                (await mediator.Send(new GetClinicsQuery(), ct)).ToHttpResult())
            .WithName("ListClinics");

        group.MapGet("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken ct) =>
                (await mediator.Send(new GetClinicByIdQuery(id), ct)).ToHttpResult())
            .WithName("GetClinic");

        group.MapPost("/", async (CreateClinicRequest request, IMediator mediator, CancellationToken ct) =>
            {
                var result = await mediator.Send(new CreateClinicCommand(request.Name, request.Address), ct);
                return result.Match(
                    created => Results.Created($"/clinics/{created.Id}", created),
                    error => error.ToProblem());
            })
            .RequireAuthorization(PolicyNames.AdministratorOrAbove)
            .WithValidation<CreateClinicRequest>()
            .WithName("CreateClinic");

        group.MapPut("/{id:guid}", async (Guid id, UpdateClinicRequest request, IMediator mediator, CancellationToken ct) =>
                (await mediator.Send(new UpdateClinicCommand(id, request.Name, request.Address), ct)).ToHttpResult())
            .RequireAuthorization(PolicyNames.AdministratorOrAbove)
            .WithValidation<UpdateClinicRequest>()
            .WithName("UpdateClinic");

        group.MapDelete("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken ct) =>
                (await mediator.Send(new DeactivateClinicCommand(id), ct)).ToHttpResult())
            .RequireAuthorization(PolicyNames.AdministratorOrAbove)
            .WithName("DeactivateClinic");

        return app;
    }
}
