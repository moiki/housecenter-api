using HouseCenter.Api.Common.Pagination;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.Patients.Sessions;

public sealed record ListAttentionSessionsQuery(
    Guid PatientId,
    PageQuery Paging,
    AttentionType? Type,
    SessionStatus? Status,
    DateTime? From,
    DateTime? To) : IRequest<Result<PagedResult<AttentionSessionResponse>>>;

public sealed class ListAttentionSessionsHandler(HouseCenterDbContext db)
    : IRequestHandler<ListAttentionSessionsQuery, Result<PagedResult<AttentionSessionResponse>>>
{
    public async Task<Result<PagedResult<AttentionSessionResponse>>> Handle(
        ListAttentionSessionsQuery request, CancellationToken ct)
    {
        if (!await db.Patients.AnyAsync(p => p.Id == request.PatientId, ct))
            return Error.NotFound("patients.not_found", "Patient not found");

        var query = db.AttentionSessions
            .AsNoTracking()
            .Where(s => s.PatientId == request.PatientId);

        // Pattern-match into a local so EF compares against a non-nullable constant
        if (request.Type is { } type) query = query.Where(s => s.AttentionType == type);
        if (request.Status is { } status) query = query.Where(s => s.Status == status);
        if (request.From is { } from) query = query.Where(s => s.SessionDate >= from);
        if (request.To is { } to) query = query.Where(s => s.SessionDate <= to);

        var projected = query
            .OrderByDescending(s => s.SessionDate)
            .Select(s => new AttentionSessionResponse(
                s.Id, s.PatientId, s.CollaboratorId,
                s.Collaborator.FirstName + " " + s.Collaborator.LastName,
                s.ClinicId, s.WorkRouteId, s.AttentionType.ToString(),
                s.SessionDate, s.DurationMinutes, s.Notes,
                s.Status.ToString(), s.IsActive));

        return await projected.ToPagedResultAsync(request.Paging, ct);
    }
}
