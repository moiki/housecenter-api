using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Constants;
using HouseCenter.Api.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.Patients.Sessions;

public sealed record CreateAttentionSessionCommand(
    Guid PatientId,
    Guid CollaboratorId,
    Guid? ClinicId,
    Guid? WorkRouteId,
    AttentionType AttentionType,
    DateTime SessionDate,
    int? DurationMinutes,
    string? Notes) : IRequest<Result<AttentionSessionResponse>>;

public sealed class CreateAttentionSessionHandler(HouseCenterDbContext db)
    : IRequestHandler<CreateAttentionSessionCommand, Result<AttentionSessionResponse>>
{
    public async Task<Result<AttentionSessionResponse>> Handle(
        CreateAttentionSessionCommand request, CancellationToken ct)
    {
        // Defense in depth: the endpoint validator already rejects this for HTTP
        // callers; the handler keeps the invariant for any other entry point.
        if (request.ClinicId is null && request.WorkRouteId is null)
            return Error.Validation("sessions.location_required",
                "Either ClinicId or WorkRouteId must be provided");

        if (!await db.Patients.AnyAsync(p => p.Id == request.PatientId, ct))
            return Error.NotFound("patients.not_found", "Patient not found");

        var collaborator = await db.Users
            .Include(u => u.Roles)
            .SingleOrDefaultAsync(u => u.Id == request.CollaboratorId, ct);
        if (collaborator is null)
            return Error.NotFound("users.not_found", "Collaborator not found");

        if (!collaborator.Roles.Any(r => r.Name == RoleNames.Member))
            return Error.Validation("sessions.not_a_member",
                "Collaborator must have the Member role");

        if (request.ClinicId is not null &&
            !await db.Clinics.AnyAsync(c => c.Id == request.ClinicId, ct))
            return Error.NotFound("clinics.not_found", "Clinic not found");

        if (request.WorkRouteId is not null &&
            !await db.WorkRoutes.AnyAsync(w => w.Id == request.WorkRouteId, ct))
            return Error.NotFound("work_routes.not_found", "Work route not found");

        var session = new AttentionSession
        {
            PatientId = request.PatientId,
            CollaboratorId = request.CollaboratorId,
            ClinicId = request.ClinicId,
            WorkRouteId = request.WorkRouteId,
            AttentionType = request.AttentionType,
            SessionDate = request.SessionDate,
            DurationMinutes = request.DurationMinutes,
            Notes = request.Notes
        };

        db.AttentionSessions.Add(session);
        await db.SaveChangesAsync(ct);

        return new AttentionSessionResponse(
            session.Id, session.PatientId, session.CollaboratorId,
            $"{collaborator.FirstName} {collaborator.LastName}",
            session.ClinicId, session.WorkRouteId, session.AttentionType.ToString(),
            session.SessionDate, session.DurationMinutes, session.Notes,
            session.Status.ToString(), session.IsActive);
    }
}
