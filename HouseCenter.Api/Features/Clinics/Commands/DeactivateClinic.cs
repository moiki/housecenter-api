using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Common.Persistence;
using HouseCenter.Api.Domain.Entities;
using MediatR;

namespace HouseCenter.Api.Features.Clinics.Commands;

public sealed record DeactivateClinicCommand(Guid Id) : IRequest<Result>;

public sealed class DeactivateClinicHandler(IRepository<Clinic> clinics)
    : IRequestHandler<DeactivateClinicCommand, Result>
{
    private static readonly Error NotFound = Error.NotFound("clinics.not_found", "Clinic not found");

    public async Task<Result> Handle(DeactivateClinicCommand request, CancellationToken ct)
    {
        var clinic = await clinics.GetByIdAsync(request.Id, ct);
        if (clinic is null) return NotFound;

        clinic.IsActive = false;
        await clinics.SaveChangesAsync(ct);

        return Result.Success();
    }
}
