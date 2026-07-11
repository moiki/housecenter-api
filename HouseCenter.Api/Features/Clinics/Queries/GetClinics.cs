using HouseCenter.Api.Common.Pagination;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Common.Persistence;
using HouseCenter.Api.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.Clinics.Queries;

public sealed record GetClinicsQuery(PageQuery Paging) : IRequest<Result<PagedResult<ClinicResponse>>>;

public sealed class GetClinicsHandler(IRepository<Clinic> clinics)
    : IRequestHandler<GetClinicsQuery, Result<PagedResult<ClinicResponse>>>
{
    public async Task<Result<PagedResult<ClinicResponse>>> Handle(GetClinicsQuery request, CancellationToken ct)
    {
        var query = clinics.Query()
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new ClinicResponse(c.Id, c.Name, c.Address, c.IsActive));

        return await query.ToPagedResultAsync(request.Paging, ct);
    }
}
