using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Common.Persistence;
using HouseCenter.Api.Domain.Entities;
using MediatR;

namespace HouseCenter.Api.Features.Clinics.Commands;

public sealed record UpdateClinicCommand(Guid Id, string Name, string Address) : IRequest<Result<ClinicResponse>>;

public sealed class UpdateClinicHandler(IRepository<Clinic> clinics)
    : IRequestHandler<UpdateClinicCommand, Result<ClinicResponse>>
{
    private static readonly Error NotFound = Error.NotFound("clinics.not_found", "Clinic not found");

    public async Task<Result<ClinicResponse>> Handle(UpdateClinicCommand request, CancellationToken ct)
    {
        var clinic = await clinics.GetByIdAsync(request.Id, ct);
        if (clinic is null) return NotFound;

        clinic.Name = request.Name;
        clinic.Address = request.Address;
        await clinics.SaveChangesAsync(ct);

        return new ClinicResponse(clinic.Id, clinic.Name, clinic.Address, clinic.IsActive);
    }
}
