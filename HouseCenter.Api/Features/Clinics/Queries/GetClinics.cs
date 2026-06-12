using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Common.Persistence;
using HouseCenter.Api.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.Clinics.Queries;

public sealed record GetClinicsQuery : IRequest<Result<List<ClinicResponse>>>;

public sealed class GetClinicsHandler(IRepository<Clinic> clinics)
    : IRequestHandler<GetClinicsQuery, Result<List<ClinicResponse>>>
{
    public async Task<Result<List<ClinicResponse>>> Handle(GetClinicsQuery request, CancellationToken ct) =>
        await clinics.Query()
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new ClinicResponse(c.Id, c.Name, c.Address, c.IsActive))
            .ToListAsync(ct);
}
