namespace HouseCenter.Api.Common.Push;

// Fallback when FCM isn't configured (no Push:CredentialsJson) — logs instead of sending,
// so the whole push pipeline runs in dev/CI without a Firebase project. Mirrors the
// SmtpEmailService "no host → log" behaviour.
public sealed class LoggingPushSender(ILogger<LoggingPushSender> logger) : IPushSender
{
    public Task<IReadOnlyCollection<string>> SendAsync(
        IReadOnlyCollection<string> tokens, string title, string body,
        IReadOnlyDictionary<string, string>? data, CancellationToken ct = default)
    {
        logger.LogInformation(
            "Push not configured — would send '{Title}' to {Count} token(s)", title, tokens.Count);
        return Task.FromResult<IReadOnlyCollection<string>>([]);
    }
}
