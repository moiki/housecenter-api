using System.Collections.Concurrent;
using HouseCenter.Api.Common.Translation;
using HouseCenter.Api.Domain.Entities;

namespace HouseCenter.Api.Tests;

public sealed record TranslateCall(string Text, Language TargetLanguage, Language? SourceLanguage);

/// <summary>
/// Test double for <see cref="ITranslationClient"/>: records calls instead of hitting Google,
/// and returns a canned "[{TargetLanguage}] {text}" translation so tests can assert on it
/// without needing real GCP credentials.
/// </summary>
public sealed class RecordingTranslationClient : ITranslationClient
{
    private readonly ConcurrentQueue<TranslateCall> _calls = new();

    public IReadOnlyCollection<TranslateCall> Calls => _calls;

    public Task<TranslationResult> TranslateAsync(
        string text, Language targetLanguage, Language? sourceLanguage, CancellationToken ct = default)
    {
        _calls.Enqueue(new TranslateCall(text, targetLanguage, sourceLanguage));
        var detected = sourceLanguage ?? (targetLanguage == Language.En ? Language.Es : Language.En);
        return Task.FromResult(new TranslationResult($"[{targetLanguage}] {text}", detected));
    }
}
