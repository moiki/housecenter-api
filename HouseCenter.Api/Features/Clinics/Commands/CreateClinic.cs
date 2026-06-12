using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Common.Persistence;
using HouseCenter.Api.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.Clinics.Commands;

public sealed record CreateClinicCommand(string Name, string Address) : IRequest<Result<ClinicResponse>>;

public sealed class CreateClinicHandler(IRepository<Clinic> clinics)
    : IRequestHandler<CreateClinicCommand, Result<ClinicResponse>>
{
    public async Task<Result<ClinicResponse>> Handle(CreateClinicCommand request, CancellationToken ct)
    {
        var nameTaken = await clinics.Query()
            .AnyAsync(c => c.Name == request.Name && c.IsActive, ct);
        if (nameTaken)
            return Error.Conflict("clinics.name_taken", "A clinic with this name already exists");

        var clinic = new Clinic { Name = request.Name, Address = request.Address };
        clinics.Add(clinic);
        await clinics.SaveChangesAsync(ct);

        return new ClinicResponse(clinic.Id, clinic.Name, clinic.Address, clinic.IsActive);
    }
}
