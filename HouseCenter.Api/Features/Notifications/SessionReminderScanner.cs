using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Entities;
using HouseCenter.Api.Features.Notifications.Events;
using HouseCenter.Api.Options;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HouseCenter.Api.Features.Notifications;

// The reminder logic, deliberately separate from the BackgroundService so it can be tested
// directly without driving a timer. Scoped: it owns a DbContext + IPublisher for one scan.
public sealed class SessionReminderScanner(
    HouseCenterDbContext db,
    IPublisher publisher,
    IOptions<SessionReminderOptions> options)
{
    public async Task<int> ScanAndNotifyAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var windowEnd = now.AddHours(options.Value.UpcomingWindowHours);

        // Due = Scheduled, starting inside the window, not yet reminded. AsNoTracking so
        // the per-notification SaveChanges in the handlers doesn't accidentally flush these.
        var due = await db.AttentionSessions
            .AsNoTracking()
            .Where(s => s.Status == SessionStatus.Scheduled
                        && s.ReminderSentAt == null
                        && s.SessionDate > now
                        && s.SessionDate <= windowEnd)
            .Select(s => new { s.Id, s.CollaboratorId, s.SessionDate })
            .ToListAsync(ct);

        if (due.Count == 0) return 0;

        foreach (var s in due)
            await publisher.Publish(new SessionUpcomingEvent(s.Id, s.CollaboratorId, s.SessionDate), ct);

        // Mark reminded in one set-based UPDATE — independent of the change tracker, so it
        // can't double-fire on the next scan. At-least-once: marked after publishing, so a
        // crash mid-scan re-reminds rather than silently dropping the reminder.
        var ids = due.Select(s => s.Id).ToList();
        await db.AttentionSessions
            .Where(s => ids.Contains(s.Id))
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.ReminderSentAt, now), ct);

        return due.Count;
    }
}
