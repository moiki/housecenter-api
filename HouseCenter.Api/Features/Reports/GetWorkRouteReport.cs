using System.Security.Claims;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Constants;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.Reports;

public sealed record GetWorkRouteReportQuery(
    Guid WorkRouteId,
    ClaimsPrincipal Principal) : IRequest<Result<WorkRouteReportResponse>>;

public sealed class GetWorkRouteReportHandler(HouseCenterDbContext db)
    : IRequestHandler<GetWorkRouteReportQuery, Result<WorkRouteReportResponse>>
{
    public async Task<Result<WorkRouteReportResponse>> Handle(
        GetWorkRouteReportQuery request, CancellationToken ct)
    {
        var isManagement = request.Principal.IsInRole(RoleNames.Administrator)
                           || request.Principal.IsInRole(RoleNames.Owner);

        // Doctors are remote and not tied to routes (spec R3)
        if (!isManagement && request.Principal.IsInRole(RoleNames.Doctor))
            return Error.Forbidden("reports.doctor_no_routes",
                "Doctors do not have access to work route reports");

        var route = await db.WorkRoutes
            .AsNoTracking()
            .SingleOrDefaultAsync(w => w.Id == request.WorkRouteId, ct);
        if (route is null)
            return Error.NotFound("work_routes.not_found", "Work route not found");

        var since = DateTime.UtcNow.AddDays(-30);

        // Stops are derived from patient assignment (Patient.WorkRouteId), not a
        // separately-maintained list — see WorkRouteResponse.Stops.
        var stopsCount = await db.Patients
            .CountAsync(p => p.WorkRouteId == request.WorkRouteId, ct);
        // Roster count: reads db.Collaborators directly by WorkRouteId, INCLUDING legacy
        // rows with UserId == null — same intentional semantics as GetClinicReport's
        // collaboratorsAssigned. Not a bug; see design.md Decision 5.
        var collaboratorsOnRoute = await db.Collaborators
            .CountAsync(c => c.WorkRouteId == request.WorkRouteId, ct);
        var sessionsLast30Days = await db.AttentionSessions
            .CountAsync(s => s.WorkRouteId == request.WorkRouteId && s.SessionDate >= since, ct);
        var patientsVisited = await db.AttentionSessions
            .Where(s => s.WorkRouteId == request.WorkRouteId && s.SessionDate >= since)
            .Select(s => s.PatientId)
            .Distinct()
            .CountAsync(ct);

        // Same roster source as collaboratorsOnRoute above — a legacy row's name still
        // appears here (its own FirstName/LastName), independent of any User link.
        List<string>? collaboratorNames = null;
        if (isManagement)
            collaboratorNames = await db.Collaborators
                .Where(c => c.WorkRouteId == request.WorkRouteId)
                .OrderBy(c => c.FirstName).ThenBy(c => c.LastName)
                .Select(c => c.FirstName + " " + c.LastName)
                .ToListAsync(ct);

        return new WorkRouteReportResponse(
            request.WorkRouteId, route.RouteName, stopsCount,
            collaboratorsOnRoute, sessionsLast30Days, patientsVisited, collaboratorNames);
    }
}
