using HouseCenter.Api.Domain.Entities;

namespace HouseCenter.Api.Common.Translation;

// Fallback when Google Cloud Translation isn't configured (no Translation:ProjectId) — echoes
// the input back instead of calling Google, so the whole translation pipeline runs in dev/CI
// without a GCP project. Mirrors LoggingPushSender / SmtpEmailService's "no host → log" idea.
public sealed class LoggingTranslationClient(ILogger<LoggingTranslationClient> logger) : ITranslationClient
{
    public Task<TranslationResult> TranslateAsync(
        string text, Language targetLanguage, Language? sourceLanguage, CancellationToken ct = default)
    {
        logger.LogInformation(
            "Translation not configured — would translate {Length} char(s) to {TargetLanguage}",
            text.Length, targetLanguage);
        return Task.FromResult(new TranslationResult(text, sourceLanguage));
    }
}
