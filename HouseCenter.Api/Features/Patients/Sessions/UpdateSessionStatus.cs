using System.Security.Claims;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Constants;
using HouseCenter.Api.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.Patients.Sessions;

public sealed record UpdateSessionStatusCommand(
    Guid PatientId,
    Guid SessionId,
    SessionStatus Status,
    int? DurationMinutes,
    string? Notes,
    ClaimsPrincipal Principal) : IRequest<Result<AttentionSessionResponse>>;

public sealed class UpdateSessionStatusHandler(HouseCenterDbContext db)
    : IRequestHandler<UpdateSessionStatusCommand, Result<AttentionSessionResponse>>
{
    public async Task<Result<AttentionSessionResponse>> Handle(
        UpdateSessionStatusCommand request, CancellationToken ct)
    {
        var session = await db.AttentionSessions
            .Include(s => s.Collaborator)
            .SingleOrDefaultAsync(s => s.Id == request.SessionId && s.PatientId == request.PatientId, ct);
        if (session is null)
            return Error.NotFound("sessions.not_found", "Session not found");

        // Resource-level authorization: the endpoint policy lets any Member in;
        // ownership (or admin rank) can only be decided here, where the session is known.
        var userId = Guid.Parse(request.Principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var isAdmin = request.Principal.IsInRole(RoleNames.Administrator)
                      || request.Principal.IsInRole(RoleNames.Owner);
        if (!isAdmin && session.CollaboratorId != userId)
            return Error.Forbidden("sessions.not_owner",
                "Only the session's collaborator or an administrator can update its status");

        // Status machine: a session leaves Scheduled exactly once, and never returns.
        if (session.Status != SessionStatus.Scheduled)
            return Error.Conflict("sessions.invalid_transition",
                $"Cannot change the status of a {session.Status} session");
        if (request.Status == SessionStatus.Scheduled)
            return Error.Conflict("sessions.invalid_transition",
                "A session cannot transition back to Scheduled");

        session.Status = request.Status;

        // Duration only makes sense for sessions that actually happened
        if (request.Status == SessionStatus.Completed && request.DurationMinutes is not null)
            session.DurationMinutes = request.DurationMinutes;
        if (request.Notes is not null)
            session.Notes = request.Notes;

        await db.SaveChangesAsync(ct);

        return new AttentionSessionResponse(
            session.Id, session.PatientId, session.CollaboratorId,
            $"{session.Collaborator.FirstName} {session.Collaborator.LastName}",
            session.ClinicId, session.WorkRouteId, session.AttentionType.ToString(),
            session.SessionDate, session.DurationMinutes, session.Notes,
            session.Status.ToString(), session.IsActive);
    }
}
