using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.Reports;

public sealed record GetSummaryReportQuery : IRequest<Result<SummaryReportResponse>>;

public sealed class GetSummaryReportHandler(HouseCenterDbContext db)
    : IRequestHandler<GetSummaryReportQuery, Result<SummaryReportResponse>>
{
    public async Task<Result<SummaryReportResponse>> Handle(
        GetSummaryReportQuery request, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        // Sequential awaits on purpose: one scoped DbContext cannot run queries
        // in parallel (spec R6). Each is a single COUNT — cheap round trips.
        // Total = soft-deleted included, hence IgnoreQueryFilters; the global
        // filter would silently turn "total" into "active".
        var totalPatients = await db.Patients.IgnoreQueryFilters().CountAsync(ct);
        var activePatients = await db.Patients.CountAsync(ct);
        var sessionsThisMonth = await db.AttentionSessions
            .CountAsync(s => s.SessionDate >= monthStart, ct);
        // Org-wide roster count: reads db.Collaborators directly, INCLUDING legacy rows
        // with UserId == null (real staff who never log in). This is intentional (design.md
        // Decision 5), not a discrepancy with GetSessionPeriodReport's byCollaborator
        // breakdown — that breakdown resolves through the Collaborator.UserId link and can
        // only ever include linked staff who generated sessions, so it is a distinct,
        // smaller population by construction: roster count >= collaborators-with-sessions.
        var collaborators = await db.Collaborators.CountAsync(ct);
        var clinics = await db.Clinics.CountAsync(ct);
        var workRoutes = await db.WorkRoutes.CountAsync(ct);
        var activeTreatments = await db.Treatments
            .CountAsync(t => t.Status == TreatmentStatus.Active, ct);

        var sessionsByType = await db.AttentionSessions
            .GroupBy(s => s.AttentionType)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Type.ToString(), x => x.Count, ct);

        return new SummaryReportResponse(
            totalPatients, activePatients, sessionsThisMonth, collaborators,
            clinics, workRoutes, activeTreatments, sessionsByType);
    }
}
