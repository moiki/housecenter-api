using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Entities;
using HouseCenter.Api.Features.Notifications.Events;
using MediatR;

namespace HouseCenter.Api.Features.Notifications.Handlers;

// Session channel handlers: notify the session's collaborator and announce via the same
// NotificationCreatedEvent fan-out, so email (and later push) deliver with no extra code.
// Best-effort (spec R3): failures are logged and swallowed.

public sealed class SessionMissedInAppHandler(
    HouseCenterDbContext db,
    IPublisher publisher,
    ILogger<SessionMissedInAppHandler> logger)
    : INotificationHandler<SessionMissedEvent>
{
    public async Task Handle(SessionMissedEvent e, CancellationToken ct)
    {
        try
        {
            var notification = new Notification
            {
                RecipientUserId = e.CollaboratorId,
                Type = NotificationType.SessionMissed,
                Title = "Session marked as missed",
                Body = $"The session scheduled for {e.SessionDate:yyyy-MM-dd HH:mm} was marked as missed.",
                ReferenceType = "AttentionSession",
                ReferenceId = e.SessionId
            };
            db.Notifications.Add(notification);
            await db.SaveChangesAsync(ct);
            await NotificationFanout.AnnounceAsync(publisher, [notification], ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create in-app notification for missed session {Id}", e.SessionId);
        }
    }
}

public sealed class SessionUpcomingInAppHandler(
    HouseCenterDbContext db,
    IPublisher publisher,
    ILogger<SessionUpcomingInAppHandler> logger)
    : INotificationHandler<SessionUpcomingEvent>
{
    public async Task Handle(SessionUpcomingEvent e, CancellationToken ct)
    {
        try
        {
            var notification = new Notification
            {
                RecipientUserId = e.CollaboratorId,
                Type = NotificationType.SessionUpcoming,
                Title = "Upcoming session reminder",
                Body = $"You have a session scheduled for {e.SessionDate:yyyy-MM-dd HH:mm}.",
                ReferenceType = "AttentionSession",
                ReferenceId = e.SessionId
            };
            db.Notifications.Add(notification);
            await db.SaveChangesAsync(ct);
            await NotificationFanout.AnnounceAsync(publisher, [notification], ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create in-app notification for upcoming session {Id}", e.SessionId);
        }
    }
}
