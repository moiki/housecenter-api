using System.Security.Claims;
using HouseCenter.Api.Common.Behaviors;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Common.Translation;
using HouseCenter.Api.Domain.Entities;
using MediatR;

namespace HouseCenter.Api.Features.Translation;

// Takes only text, never an entity ID — the caller already loaded that text under its own
// read permissions, so no extra authorization check is needed against a source entity, and
// this stays a single generic endpoint instead of three near-duplicate entity-scoped ones.
public sealed record TranslateTextCommand(
    string Text,
    Language TargetLanguage,
    Language? SourceLanguage,
    ClaimsPrincipal Principal) : IRequest<Result<TranslateTextResponse>>, IRateLimitedRequest
{
    public string RateLimitKey => Principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
}

public sealed class TranslateTextHandler(ITranslationClient client)
    : IRequestHandler<TranslateTextCommand, Result<TranslateTextResponse>>
{
    public async Task<Result<TranslateTextResponse>> Handle(TranslateTextCommand request, CancellationToken ct)
    {
        var result = await client.TranslateAsync(request.Text, request.TargetLanguage, request.SourceLanguage, ct);
        return new TranslateTextResponse(
            result.TranslatedText, request.TargetLanguage.ToString(), result.DetectedSourceLanguage?.ToString());
    }
}
