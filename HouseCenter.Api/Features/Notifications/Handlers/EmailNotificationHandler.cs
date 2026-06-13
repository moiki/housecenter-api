using HouseCenter.Api.Common.Email;
using HouseCenter.Api.Data;
using HouseCenter.Api.Features.Notifications.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.Notifications.Handlers;

// Email channel: delivers any created notification out-of-band. Looks up the recipient's
// address and sends a templated email. Best-effort (spec R3) — a send failure is logged
// and swallowed so it never affects the action that produced the notification.
// NOTE: the send runs in-request (consistent with the existing invitation flow); moving
// it to a background outbox is future work.
public sealed class EmailNotificationHandler(
    HouseCenterDbContext db,
    IEmailService emailService,
    ILogger<EmailNotificationHandler> logger)
    : INotificationHandler<NotificationCreatedEvent>
{
    public async Task Handle(NotificationCreatedEvent e, CancellationToken ct)
    {
        try
        {
            var recipient = await db.Users
                .AsNoTracking()
                .Where(u => u.Id == e.RecipientUserId)
                .Select(u => new { u.Email })
                .SingleOrDefaultAsync(ct);

            if (recipient is null || string.IsNullOrWhiteSpace(recipient.Email))
            {
                logger.LogWarning("No email on file for notification recipient {RecipientId}", e.RecipientUserId);
                return;
            }

            await emailService.SendAsync(
                recipient.Email, e.Title, EmailTemplates.Notification(e.Title, e.Body), ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to email notification {NotificationId} to {RecipientId}",
                e.NotificationId, e.RecipientUserId);
        }
    }
}
