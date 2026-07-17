using System.Security.Claims;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Common.Validation;
using HouseCenter.Api.Domain.Constants;
using MediatR;

namespace HouseCenter.Api.Features.Translation;

public static class TranslationEndpoints
{
    public static IEndpointRouteBuilder MapTranslationEndpoints(this IEndpointRouteBuilder app)
    {
        // StaffOnly: sponsors already see only anonymized aggregates, never individual
        // clinical text — extending translate to them would quietly break that boundary.
        app.MapPost("/translate", async (TranslateTextRequest request, ClaimsPrincipal principal,
                IMediator mediator, CancellationToken ct) =>
                (await mediator.Send(new TranslateTextCommand(
                    request.Text, request.TargetLanguage, request.SourceLanguage, principal), ct)).ToHttpResult())
            .WithTags("Translation")
            .RequireAuthorization(PolicyNames.StaffOnly)
            .WithValidation<TranslateTextRequest>()
            .WithName("TranslateText");

        return app;
    }
}
