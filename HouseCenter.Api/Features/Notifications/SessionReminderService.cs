using HouseCenter.Api.Options;
using Microsoft.Extensions.Options;

namespace HouseCenter.Api.Features.Notifications;

// Time-driven trigger: the only notification source not started by an HTTP request.
// A singleton hosted service that ticks on a PeriodicTimer and runs the scoped scanner
// inside a fresh DI scope each time (the scanner's DbContext is scoped — can't be held
// by a singleton). The .NET equivalent of a managed setInterval with graceful shutdown.
public sealed class SessionReminderService(
    IServiceProvider services,
    IOptions<SessionReminderOptions> options,
    ILogger<SessionReminderService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (!opts.Enabled)
        {
            logger.LogInformation("Session reminder scanner is disabled.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(opts.ScanIntervalMinutes));
        try
        {
            do
            {
                await RunScanAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // normal on shutdown
        }
    }

    private async Task RunScanAsync(CancellationToken ct)
    {
        try
        {
            using var scope = services.CreateScope();
            var scanner = scope.ServiceProvider.GetRequiredService<SessionReminderScanner>();
            var count = await scanner.ScanAndNotifyAsync(ct);
            if (count > 0)
                logger.LogInformation("Session reminder scan sent {Count} reminder(s)", count);
        }
        catch (OperationCanceledException)
        {
            throw; // let the loop exit on shutdown
        }
        catch (Exception ex)
        {
            // One bad scan must not kill the loop.
            logger.LogError(ex, "Session reminder scan failed");
        }
    }
}
