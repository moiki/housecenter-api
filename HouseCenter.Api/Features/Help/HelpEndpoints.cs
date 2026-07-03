using System.Security.Claims;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Common.Validation;
using HouseCenter.Api.Domain.Constants;
using MediatR;

namespace HouseCenter.Api.Features.Help;

public static class HelpEndpoints
{
    public static IEndpointRouteBuilder MapHelpEndpoints(this IEndpointRouteBuilder app)
    {
        // Any authenticated role — including Sponsor. Per-role audience filtering happens
        // inside the handlers (see ListHelpTopicsHandler). Writes (POST/PUT/DELETE) are
        // gated AdministratorOrAbove below.
        var group = app.MapGroup("/help").WithTags("Help").RequireAuthorization();

        group.MapGet("/topics", async (ClaimsPrincipal principal, IMediator mediator, CancellationToken ct) =>
                (await mediator.Send(new ListHelpTopicsQuery(principal), ct)).ToHttpResult())
            .WithName("ListHelpTopics");

        group.MapGet("/topics/{topicKey}", async (string topicKey, ClaimsPrincipal principal, IMediator mediator, CancellationToken ct) =>
                (await mediator.Send(new GetHelpTopicQuery(topicKey, principal), ct)).ToHttpResult())
            .WithName("GetHelpTopic");

        group.MapPost("/topics", async (CreateHelpTopicRequest request, IMediator mediator, CancellationToken ct) =>
            {
                var result = await mediator.Send(new CreateHelpTopicCommand(
                    request.TopicKey, request.Title, request.Summary, request.Body,
                    request.Audience, request.Category, request.DisplayOrder), ct);
                return result.Match(
                    created => Results.Created($"/help/topics/{created.TopicKey}", created),
                    error => error.ToProblem());
            })
            .RequireAuthorization(PolicyNames.AdministratorOrAbove)
            .WithValidation<CreateHelpTopicRequest>()
            .WithName("CreateHelpTopic");

        group.MapPut("/topics/{id:guid}", async (Guid id, UpdateHelpTopicRequest request, IMediator mediator, CancellationToken ct) =>
                (await mediator.Send(new UpdateHelpTopicCommand(
                    id, request.Title, request.Summary, request.Body,
                    request.Audience, request.Category, request.DisplayOrder), ct)).ToHttpResult())
            .RequireAuthorization(PolicyNames.AdministratorOrAbove)
            .WithValidation<UpdateHelpTopicRequest>()
            .WithName("UpdateHelpTopic");

        group.MapDelete("/topics/{id:guid}", async (Guid id, IMediator mediator, CancellationToken ct) =>
                (await mediator.Send(new DeleteHelpTopicCommand(id), ct)).ToHttpResult())
            .RequireAuthorization(PolicyNames.AdministratorOrAbove)
            .WithName("DeleteHelpTopic");

        return app;
    }
}
