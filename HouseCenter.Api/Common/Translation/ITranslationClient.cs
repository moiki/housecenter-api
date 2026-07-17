using HouseCenter.Api.Domain.Entities;

namespace HouseCenter.Api.Common.Translation;

public interface ITranslationClient
{
    /// <summary>
    /// Translates <paramref name="text"/> into <paramref name="targetLanguage"/>. When
    /// <paramref name="sourceLanguage"/> is null, the provider auto-detects it and reports it
    /// back in the result.
    /// </summary>
    Task<TranslationResult> TranslateAsync(
        string text,
        Language targetLanguage,
        Language? sourceLanguage,
        CancellationToken ct = default);
}

public sealed record TranslationResult(string TranslatedText, Language? DetectedSourceLanguage);
