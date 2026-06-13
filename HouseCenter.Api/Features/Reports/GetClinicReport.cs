using System.Security.Claims;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Constants;
using HouseCenter.Api.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.Reports;

public sealed record GetClinicReportQuery(
    Guid ClinicId,
    ClaimsPrincipal Principal) : IRequest<Result<ClinicReportResponse>>;

public sealed class GetClinicReportHandler(HouseCenterDbContext db)
    : IRequestHandler<GetClinicReportQuery, Result<ClinicReportResponse>>
{
    public async Task<Result<ClinicReportResponse>> Handle(
        GetClinicReportQuery request, CancellationToken ct)
    {
        var clinicName = await db.Clinics
            .Where(c => c.Id == request.ClinicId)
            .Select(c => c.Name)
            .SingleOrDefaultAsync(ct);
        if (clinicName is null)
            return Error.NotFound("clinics.not_found", "Clinic not found");

        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var patientsAttending = await db.Patients
            .CountAsync(p => p.ClinicId == request.ClinicId, ct);
        var collaboratorsAssigned = await db.Collaborators
            .CountAsync(c => c.ClinicId == request.ClinicId, ct);
        var sessionsThisMonth = await db.AttentionSessions
            .CountAsync(s => s.ClinicId == request.ClinicId && s.SessionDate >= monthStart, ct);
        var activeTreatments = await db.Treatments
            .CountAsync(t => t.Patient.ClinicId == request.ClinicId
                             && t.Status == TreatmentStatus.Active, ct);

        // Role-based projection: same aggregates for everyone, names only for
        // management (spec R2 — anonymized response for the rest).
        var isManagement = request.Principal.IsInRole(RoleNames.Administrator)
                           || request.Principal.IsInRole(RoleNames.Owner);
        List<string>? collaboratorNames = null;
        if (isManagement)
            collaboratorNames = await db.Collaborators
                .Where(c => c.ClinicId == request.ClinicId)
                .OrderBy(c => c.FirstName).ThenBy(c => c.LastName)
                .Select(c => c.FirstName + " " + c.LastName)
                .ToListAsync(ct);

        return new ClinicReportResponse(
            request.ClinicId, clinicName, patientsAttending, collaboratorsAssigned,
            sessionsThisMonth, activeTreatments, collaboratorNames);
    }
}
