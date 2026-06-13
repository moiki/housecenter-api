using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Entities;
using HouseCenter.Api.Features.Notifications.Events;
using MediatR;

namespace HouseCenter.Api.Features.Notifications.Handlers;

// In-app channel + fan-out point: each handler turns a domain event into persisted
// Notification rows (recipient resolution lives ONLY here), then announces each saved
// row via NotificationCreatedEvent so other channels (email, push) can deliver it.
// Best-effort (spec R3): failures are logged and swallowed so they can never roll back
// or fail the consultation action that raised the event.

public sealed class ConsultationMessageInAppHandler(
    HouseCenterDbContext db,
    IPublisher publisher,
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
                .Distinct()
                .ToList();

            var created = recipients.Select(recipientId => new Notification
            {
                RecipientUserId = recipientId,
                Type = NotificationType.ConsultationMessage,
                Title = "New message on a consultation",
                Body = $"New message on \"{e.Title}\".",
                ReferenceType = "Consultation",
                ReferenceId = e.ConsultationId
            }).ToList();

            db.Notifications.AddRange(created);
            await db.SaveChangesAsync(ct);
            await NotificationFanout.AnnounceAsync(publisher, created, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create in-app notification for message on consultation {Id}", e.ConsultationId);
        }
    }
}

public sealed class ConsultationOpenedInAppHandler(
    HouseCenterDbContext db,
    IPublisher publisher,
    ILogger<ConsultationOpenedInAppHandler> logger)
    : INotificationHandler<ConsultationOpenedEvent>
{
    public async Task Handle(ConsultationOpenedEvent e, CancellationToken ct)
    {
        try
        {
            var notification = new Notification
            {
                RecipientUserId = e.AssignedDoctorId,
                Type = NotificationType.ConsultationOpened,
                Title = "New consultation assigned",
                Body = $"You were assigned the consultation \"{e.Title}\".",
                ReferenceType = "Consultation",
                ReferenceId = e.ConsultationId
            };
            db.Notifications.Add(notification);
            await db.SaveChangesAsync(ct);
            await NotificationFanout.AnnounceAsync(publisher, [notification], ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create in-app notification for opened consultation {Id}", e.ConsultationId);
        }
    }
}

public sealed class ConsultationResolvedInAppHandler(
    HouseCenterDbContext db,
    IPublisher publisher,
    ILogger<ConsultationResolvedInAppHandler> logger)
    : INotificationHandler<ConsultationResolvedEvent>
{
    public async Task Handle(ConsultationResolvedEvent e, CancellationToken ct)
    {
        try
        {
            var notification = new Notification
            {
                RecipientUserId = e.OpenedByUserId,
                Type = NotificationType.ConsultationResolved,
                Title = "Consultation resolved",
                Body = $"Your consultation \"{e.Title}\" was marked resolved.",
                ReferenceType = "Consultation",
                ReferenceId = e.ConsultationId
            };
            db.Notifications.Add(notification);
            await db.SaveChangesAsync(ct);
            await NotificationFanout.AnnounceAsync(publisher, [notification], ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create in-app notification for resolved consultation {Id}", e.ConsultationId);
        }
    }
}

internal static class NotificationFanout
{
    // One NotificationCreatedEvent per persisted row → every delivery channel consumes
    // the same uniform event.
    public static async Task AnnounceAsync(
        IPublisher publisher, IEnumerable<Notification> notifications, CancellationToken ct)
    {
        foreach (var n in notifications)
            await publisher.Publish(new NotificationCreatedEvent(
                n.Id, n.RecipientUserId, n.Type, n.Title, n.Body, n.ReferenceType, n.ReferenceId), ct);
    }
}
