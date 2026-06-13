using System.Security.Claims;
using HouseCenter.Api.Common.Pagination;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Constants;
using HouseCenter.Api.Domain.Entities;
using MediatR;

namespace HouseCenter.Api.Features.Consultations;

public sealed record ListConsultationsQuery(
    PageQuery Paging,
    ConsultationStatus? Status,
    ClaimsPrincipal Principal) : IRequest<Result<PagedResult<ConsultationResponse>>>;

public sealed class ListConsultationsHandler(HouseCenterDbContext db)
    : IRequestHandler<ListConsultationsQuery, Result<PagedResult<ConsultationResponse>>>
{
    public async Task<Result<PagedResult<ConsultationResponse>>> Handle(
        ListConsultationsQuery request, CancellationToken ct)
    {
        var userId = Guid.Parse(request.Principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var isAdmin = request.Principal.IsInRole(RoleNames.Administrator)
                      || request.Principal.IsInRole(RoleNames.Owner);

        var query = db.MedicalConsultations.AsQueryable();

        // Role-filtered query: same endpoint, different WHERE per role.
        // The role flags are captured as constants, so EF parameterizes them
        // and the dead branch collapses in the SQL plan.
        if (!isAdmin)
        {
            var isDoctor = request.Principal.IsInRole(RoleNames.Doctor);
            var isMember = request.Principal.IsInRole(RoleNames.Member);
            query = query.Where(c =>
                (isDoctor && c.AssignedDoctorId == userId) ||
                (isMember && c.OpenedByUserId == userId));
        }

        if (request.Status is { } status)
            query = query.Where(c => c.Status == status);

        var projected = query
            .OrderByDescending(c => c.CreatedDate)
            .Select(c => new ConsultationResponse(
                c.Id, c.PatientId, c.TreatmentId, c.OpenedByUserId, c.AssignedDoctorId,
                c.Title, c.Status.ToString(), c.ResolvedAt, c.IsActive));

        return await projected.ToPagedResultAsync(request.Paging, ct);
    }
}
