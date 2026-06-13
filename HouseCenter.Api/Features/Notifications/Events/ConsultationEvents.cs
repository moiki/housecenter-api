using MediatR;

namespace HouseCenter.Api.Features.Notifications.Events;

// Domain events: published by command handlers AFTER a successful SaveChanges, consumed
// by one INotificationHandler per channel (in-app now; email/push later). The publisher
// knows nothing about channels — that is the whole point of routing through MediatR.

public sealed record ConsultationMessagePostedEvent(
    Guid ConsultationId,
    string Title,
    Guid AuthorId,
    Guid OpenedByUserId,
    Guid AssignedDoctorId) : INotification;

public sealed record ConsultationOpenedEvent(
    Guid ConsultationId,
    string Title,
    Guid AssignedDoctorId) : INotification;

public sealed record ConsultationResolvedEvent(
    Guid ConsultationId,
    string Title,
    Guid OpenedByUserId) : INotification;
