using System.Security.Claims;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Constants;
using HouseCenter.Api.Domain.Entities;
using HouseCenter.Api.Features.Notifications.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.Consultations;

public sealed record UpdateConsultationStatusCommand(
    Guid ConsultationId,
    ConsultationStatus Status,
    ClaimsPrincipal Principal) : IRequest<Result<ConsultationResponse>>;

public sealed class UpdateConsultationStatusHandler(HouseCenterDbContext db, IPublisher publisher)
    : IRequestHandler<UpdateConsultationStatusCommand, Result<ConsultationResponse>>
{
    public async Task<Result<ConsultationResponse>> Handle(
        UpdateConsultationStatusCommand request, CancellationToken ct)
    {
        // Open → UnderReview is automatic (doctor's first reply); Resolved is the
        // only transition a user may request explicitly.
        if (request.Status != ConsultationStatus.Resolved)
            return Error.Validation("consultations.invalid_status",
                "Only Resolved is a valid manual status change");

        var consultation = await db.MedicalConsultations
            .SingleOrDefaultAsync(c => c.Id == request.ConsultationId, ct);
        if (consultation is null)
            return Error.NotFound("consultations.not_found", "Consultation not found");

        var userId = Guid.Parse(request.Principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var isAdmin = request.Principal.IsInRole(RoleNames.Administrator)
                      || request.Principal.IsInRole(RoleNames.Owner);
        if (!isAdmin && consultation.AssignedDoctorId != userId)
            return Error.Forbidden("consultations.not_assigned_doctor",
                "Only the assigned doctor or an administrator can resolve a consultation");

        if (consultation.Status == ConsultationStatus.Resolved)
            return Error.Conflict("consultations.already_resolved",
                "Consultation is already resolved");

        consultation.Status = ConsultationStatus.Resolved;
        consultation.ResolvedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await publisher.Publish(new ConsultationResolvedEvent(
            consultation.Id, consultation.Title, consultation.OpenedByUserId), ct);

        return ConsultationMapping.ToResponse(consultation);
    }
}
