using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Entities;
using HouseCenter.Api.Features.Notifications.Events;
using MediatR;

namespace HouseCenter.Api.Features.Notifications.Handlers;

// In-app channel: each handler turns a domain event into persisted Notification rows.
// Best-effort (spec R3): a failure here is logged and swallowed so it can never roll back
// or fail the consultation action that raised the event.

public sealed class ConsultationMessageInAppHandler(
    HouseCenterDbContext db,
    ILogger<ConsultationMessageInAppHandler> logger)
    : INotificationHandler<ConsultationMessagePostedEvent>
{
    public async Task Handle(ConsultationMessagePostedEvent e, CancellationToken ct)
    {
        try
        {
            // Notify every participant except whoever wrote the message. When a
            // participant posts, that is the other one; when an admin posts, it is both.
            var recipients = new[] { e.OpenedByUserId, e.AssignedDoctorId }
                .Where(id => id != e.AuthorId)
                .Distinct();

            foreach (var recipientId in recipients)
            {
                db.Notifications.Add(new Notification
                {
                    RecipientUserId = recipientId,
                    Type = NotificationType.ConsultationMessage,
                    Title = "New message on a consultation",
                    Body = $"New message on \"{e.Title}\".",
                    ReferenceType = "Consultation",
                    ReferenceId = e.ConsultationId
                });
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create in-app notification for message on consultation {Id}", e.ConsultationId);
        }
    }
}

public sealed class ConsultationOpenedInAppHandler(
    HouseCenterDbContext db,
    ILogger<ConsultationOpenedInAppHandler> logger)
    : INotificationHandler<ConsultationOpenedEvent>
{
    public async Task Handle(ConsultationOpenedEvent e, CancellationToken ct)
    {
        try
        {
            db.Notifications.Add(new Notification
            {
                RecipientUserId = e.AssignedDoctorId,
                Type = NotificationType.ConsultationOpened,
                Title = "New consultation assigned",
                Body = $"You were assigned the consultation \"{e.Title}\".",
                ReferenceType = "Consultation",
                ReferenceId = e.ConsultationId
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create in-app notification for opened consultation {Id}", e.ConsultationId);
        }
    }
}

public sealed class ConsultationResolvedInAppHandler(
    HouseCenterDbContext db,
    ILogger<ConsultationResolvedInAppHandler> logger)
    : INotificationHandler<ConsultationResolvedEvent>
{
    public async Task Handle(ConsultationResolvedEvent e, CancellationToken ct)
    {
        try
        {
            db.Notifications.Add(new Notification
            {
                RecipientUserId = e.OpenedByUserId,
                Type = NotificationType.ConsultationResolved,
                Title = "Consultation resolved",
                Body = $"Your consultation \"{e.Title}\" was marked resolved.",
                ReferenceType = "Consultation",
                ReferenceId = e.ConsultationId
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create in-app notification for resolved consultation {Id}", e.ConsultationId);
        }
    }
}
