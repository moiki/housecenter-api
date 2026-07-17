using Google.Cloud.Translate.V3;
using HouseCenter.Api.Domain.Entities;
using HouseCenter.Api.Options;
using Microsoft.Extensions.Options;

namespace HouseCenter.Api.Common.Translation;

// Real Google Cloud Translation API v3 (Advanced) client. "global" is used as the location —
// Advanced's basic text-translation call doesn't need a region-pinned endpoint, so this avoids
// forcing a data-residency choice the NGO hasn't made. Deterministic (not an LLM), so it won't
// paraphrase clinical text the way a generative model could.
public sealed class GoogleTranslationClient : ITranslationClient
{
    private readonly TranslationServiceClient _client;
    private readonly string _parent;

    public GoogleTranslationClient(IOptions<TranslationOptions> options)
    {
        var config = options.Value;
        _client = new TranslationServiceClientBuilder { JsonCredentials = config.CredentialsJson }.Build();
        _parent = $"projects/{config.ProjectId}/locations/global";
    }

    public async Task<TranslationResult> TranslateAsync(
        string text, Language targetLanguage, Language? sourceLanguage, CancellationToken ct = default)
    {
        var request = new TranslateTextRequest
        {
            Parent = _parent,
            Contents = { text },
            // Consultation messages and patient/treatment comments are both rich-text HTML
            // (RHFRichText/RichTextView on the frontend) — "text/html" preserves markup and
            // translates only the text nodes; plain text has no tags, so it's translated the
            // same either way. Using "text/plain" here would mangle any HTML markup present.
            MimeType = "text/html",
            TargetLanguageCode = ToIsoCode(targetLanguage),
            SourceLanguageCode = sourceLanguage is { } lang ? ToIsoCode(lang) : "",
        };

        var response = await _client.TranslateTextAsync(request, ct);
        var translation = response.Translations[0];

        return new TranslationResult(translation.TranslatedText, FromIsoCode(translation.DetectedLanguageCode));
    }

    private static string ToIsoCode(Language language) => language switch
    {
        Language.En => "en",
        Language.Es => "es",
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, null),
    };

    private static Language? FromIsoCode(string? isoCode) => isoCode?.ToLowerInvariant() switch
    {
        "en" => Language.En,
        "es" => Language.Es,
        _ => null, // provider detected a language we don't otherwise support in the UI
    };
}
