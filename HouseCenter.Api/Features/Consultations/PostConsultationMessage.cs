using System.Security.Claims;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Constants;
using HouseCenter.Api.Domain.Entities;
using HouseCenter.Api.Features.Notifications.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.Consultations;

public sealed record PostConsultationMessageCommand(
    Guid ConsultationId,
    string Body,
    string? AttachmentUrl,
    ClaimsPrincipal Principal) : IRequest<Result<ConsultationMessageResponse>>;

public sealed class PostConsultationMessageHandler(HouseCenterDbContext db, IPublisher publisher)
    : IRequestHandler<PostConsultationMessageCommand, Result<ConsultationMessageResponse>>
{
    public async Task<Result<ConsultationMessageResponse>> Handle(
        PostConsultationMessageCommand request, CancellationToken ct)
    {
        var consultation = await db.MedicalConsultations
            .SingleOrDefaultAsync(c => c.Id == request.ConsultationId, ct);
        if (consultation is null)
            return Error.NotFound("consultations.not_found", "Consultation not found");

        var userId = Guid.Parse(request.Principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var isAdmin = request.Principal.IsInRole(RoleNames.Administrator)
                      || request.Principal.IsInRole(RoleNames.Owner);
        var isParticipant = consultation.OpenedByUserId == userId
                            || consultation.AssignedDoctorId == userId;
        if (!isAdmin && !isParticipant)
            return Error.Forbidden("consultations.not_participant",
                "Only participants can post messages in this consultation");

        if (consultation.Status == ConsultationStatus.Resolved)
            return Error.Conflict("consultations.resolved",
                "Cannot post messages to a resolved consultation");

        var message = new ConsultationMessage
        {
            ConsultationId = consultation.Id,
            AuthorId = userId,
            Body = request.Body,
            AttachmentUrl = request.AttachmentUrl
        };
        db.ConsultationMessages.Add(message);

        // Automatic transition driven by a business rule, not user input: the
        // doctor's first reply means the case is being looked at. The lifecycle
        // never moves backwards, so only Open → UnderReview happens here.
        if (userId == consultation.AssignedDoctorId
            && consultation.Status == ConsultationStatus.Open)
            consultation.Status = ConsultationStatus.UnderReview;

        await db.SaveChangesAsync(ct);

        await publisher.Publish(new ConsultationMessagePostedEvent(
            consultation.Id, consultation.Title, userId,
            consultation.OpenedByUserId, consultation.AssignedDoctorId), ct);

        var authorName = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.FirstName + " " + u.LastName)
            .SingleAsync(ct);

        return new ConsultationMessageResponse(
            message.Id, message.ConsultationId, message.AuthorId, authorName,
            message.Body, message.AttachmentUrl, message.CreatedDate);
    }
}
