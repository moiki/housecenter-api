using System.Security.Claims;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Constants;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.Reports;

public sealed record GetSessionPeriodReportQuery(
    DateOnly From,
    DateOnly To,
    ClaimsPrincipal Principal) : IRequest<Result<SessionPeriodReportResponse>>;

public sealed class GetSessionPeriodReportHandler(HouseCenterDbContext db)
    : IRequestHandler<GetSessionPeriodReportQuery, Result<SessionPeriodReportResponse>>
{
    public async Task<Result<SessionPeriodReportResponse>> Handle(
        GetSessionPeriodReportQuery request, CancellationToken ct)
    {
        if (request.From > request.To)
            return Error.Validation("reports.invalid_range",
                "'from' must be on or before 'to'");
        if (request.To.DayNumber - request.From.DayNumber > 366)
            return Error.Validation("reports.range_too_long",
                "Date range cannot exceed 366 days");

        var fromUtc = request.From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toExclusive = request.To.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var userId = Guid.Parse(request.Principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var isManagement = request.Principal.IsInRole(RoleNames.Administrator)
                           || request.Principal.IsInRole(RoleNames.Owner);

        var sessions = db.AttentionSessions
            .AsNoTracking()
            .Where(s => s.SessionDate >= fromUtc && s.SessionDate < toExclusive);

        // Role scope (spec R4): doctors see their assigned patients' sessions,
        // members their own. Sponsors see the org-wide aggregate — the buckets
        // carry no names, so nothing sensitive leaks.
        if (!isManagement)
        {
            if (request.Principal.IsInRole(RoleNames.Doctor))
                sessions = sessions.Where(
                    s => s.Patient.AssignedDoctors.Any(d => d.Id == userId));
            else if (request.Principal.IsInRole(RoleNames.Member))
                sessions = sessions.Where(s => s.CollaboratorId == userId);
        }

        // GroupBy in SQL down to day granularity; the week bucketing happens
        // in memory because "Monday-based week start" has no clean translation.
        var daily = await sessions
            .GroupBy(s => new { Date = s.SessionDate.Date, s.AttentionType, s.Status })
            .Select(g => new { g.Key.Date, g.Key.AttentionType, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);

        var weeks = daily
            .GroupBy(d => new
            {
                WeekStart = StartOfWeek(DateOnly.FromDateTime(d.Date)),
                d.AttentionType,
                d.Status
            })
            .Select(g => new WeeklySessionBucket(
                g.Key.WeekStart, g.Key.AttentionType.ToString(),
                g.Key.Status.ToString(), g.Sum(x => x.Count)))
            .OrderBy(b => b.WeekStart).ThenBy(b => b.AttentionType).ThenBy(b => b.Status)
            .ToList();

        Dictionary<string, int>? byCollaborator = null;
        if (isManagement)
            byCollaborator = await sessions
                .GroupBy(s => s.Collaborator.FirstName + " " + s.Collaborator.LastName)
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Name, x => x.Count, ct);

        return new SessionPeriodReportResponse(request.From, request.To, weeks, byCollaborator);
    }

    private static DateOnly StartOfWeek(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7)); // Monday-based
}
