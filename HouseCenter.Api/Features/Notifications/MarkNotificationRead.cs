using System.Security.Claims;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.Notifications;

public sealed record MarkNotificationReadCommand(Guid Id, ClaimsPrincipal Principal)
    : IRequest<Result<NotificationResponse>>;

public sealed class MarkNotificationReadHandler(HouseCenterDbContext db)
    : IRequestHandler<MarkNotificationReadCommand, Result<NotificationResponse>>
{
    public async Task<Result<NotificationResponse>> Handle(
        MarkNotificationReadCommand request, CancellationToken ct)
    {
        var userId = Guid.Parse(request.Principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // Scope the lookup to the caller: another user's notification reads as 404,
        // which doesn't reveal that it exists (spec R5).
        var notification = await db.Notifications
            .SingleOrDefaultAsync(n => n.Id == request.Id && n.RecipientUserId == userId, ct);
        if (notification is null)
            return Error.NotFound("notifications.not_found", "Notification not found");

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return new NotificationResponse(
            notification.Id, notification.Type.ToString(), notification.Title, notification.Body,
            notification.ReferenceType, notification.ReferenceId,
            notification.IsRead, notification.ReadAt, notification.CreatedDate);
    }
}
