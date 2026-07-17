using HouseCenter.Api.Domain.Entities;

namespace HouseCenter.Api.Features.Translation;

public sealed record TranslateTextRequest(string Text, Language TargetLanguage, Language? SourceLanguage);

// String, not Language, for the same reason UserResponse.Language is a string: a client-side
// System.Text.Json deserializer with no JsonStringEnumConverter registered (e.g. a plain
// HttpClient in tests, or the frontend's TS string-union type) reads enum-as-string fine but
// chokes on the server's ConfigureHttpJsonOptions-driven string output if the field is typed
// as the raw enum on the way out.
public sealed record TranslateTextResponse(string TranslatedText, string TargetLanguage, string? DetectedSourceLanguage);
