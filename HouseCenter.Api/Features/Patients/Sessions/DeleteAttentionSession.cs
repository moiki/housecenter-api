using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.Patients.Sessions;

public sealed record DeleteAttentionSessionCommand(Guid PatientId, Guid SessionId) : IRequest<Result>;

public sealed class DeleteAttentionSessionHandler(HouseCenterDbContext db)
    : IRequestHandler<DeleteAttentionSessionCommand, Result>
{
    public async Task<Result> Handle(DeleteAttentionSessionCommand request, CancellationToken ct)
    {
        var session = await db.AttentionSessions
            .SingleOrDefaultAsync(s => s.Id == request.SessionId && s.PatientId == request.PatientId, ct);
        if (session is null)
            return Error.NotFound("sessions.not_found", "Session not found");

        session.IsActive = false;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
