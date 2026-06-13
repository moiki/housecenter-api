using HouseCenter.Api.Domain.Entities;
using MediatR;

namespace HouseCenter.Api.Features.Notifications.Events;

// Second-level event: raised once an in-app Notification has been persisted, so every
// out-of-band channel (email now, push later) consumes ONE uniform event and recipient
// resolution lives only in the per-event in-app handlers — not duplicated per channel.
public sealed record NotificationCreatedEvent(
    Guid NotificationId,
    Guid RecipientUserId,
    NotificationType Type,
    string Title,
    string Body,
    string? ReferenceType,
    Guid? ReferenceId) : INotification;
