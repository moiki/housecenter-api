using HouseCenter.Api.Common.Push;
using HouseCenter.Api.Data;
using HouseCenter.Api.Features.Notifications.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.Notifications.Handlers;

// Push channel: third consumer of NotificationCreatedEvent (after in-app and email).
// Resolves the recipient's device tokens, pushes via FCM, and prunes any tokens the
// provider reports dead. Best-effort (spec R3). ReferenceType/ReferenceId travel in the
// data payload so the mobile app can deep-link to the consultation/session.
public sealed class PushNotificationHandler(
    HouseCenterDbContext db,
    IPushSender push,
    ILogger<PushNotificationHandler> logger)
    : INotificationHandler<NotificationCreatedEvent>
{
    public async Task Handle(NotificationCreatedEvent e, CancellationToken ct)
    {
        try
        {
            var tokens = await db.PushSubscriptions
                .AsNoTracking()
                .Where(s => s.OwnerUserId == e.RecipientUserId)
                .Select(s => s.Token)
                .ToListAsync(ct);

            if (tokens.Count == 0) return;

            var data = new Dictionary<string, string>
            {
                ["type"] = e.Type.ToString(),
                ["referenceType"] = e.ReferenceType ?? string.Empty,
                ["referenceId"] = e.ReferenceId?.ToString() ?? string.Empty
            };

            var invalid = await push.SendAsync(tokens, e.Title, e.Body, data, ct);

            if (invalid.Count > 0)
                await db.PushSubscriptions
                    .Where(s => invalid.Contains(s.Token))
                    .ExecuteDeleteAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to push notification {NotificationId} to {RecipientId}",
                e.NotificationId, e.RecipientUserId);
        }
    }
}
