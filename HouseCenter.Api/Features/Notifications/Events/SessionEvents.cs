using MediatR;

namespace HouseCenter.Api.Features.Notifications.Events;

// Session domain events. Both notify the session's collaborator and flow through the
// same NotificationCreatedEvent fan-out as the consultation events.

// Reactive: raised in-request when a session is marked Missed.
public sealed record SessionMissedEvent(
    Guid SessionId,
    Guid CollaboratorId,
    DateTime SessionDate) : INotification;

// Proactive: raised by the background scanner for a session starting soon.
public sealed record SessionUpcomingEvent(
    Guid SessionId,
    Guid CollaboratorId,
    DateTime SessionDate) : INotification;
