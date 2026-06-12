using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Common.Persistence;
using HouseCenter.Api.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.Clinics.Queries;

public sealed record GetClinicByIdQuery(Guid Id) : IRequest<Result<ClinicResponse>>;

public sealed class GetClinicByIdHandler(IRepository<Clinic> clinics)
    : IRequestHandler<GetClinicByIdQuery, Result<ClinicResponse>>
{
    private static readonly Error NotFound = Error.NotFound("clinics.not_found", "Clinic not found");

    public async Task<Result<ClinicResponse>> Handle(GetClinicByIdQuery request, CancellationToken ct)
    {
        var clinic = await clinics.Query()
            .AsNoTracking()
            .Where(c => c.Id == request.Id)
            .Select(c => new ClinicResponse(c.Id, c.Name, c.Address, c.IsActive))
            .SingleOrDefaultAsync(ct);

        return clinic is not null ? clinic : NotFound;
    }
}
