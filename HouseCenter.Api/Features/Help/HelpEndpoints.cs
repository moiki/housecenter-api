using System.Security.Claims;
using HouseCenter.Api.Common.Results;
using MediatR;

namespace HouseCenter.Api.Features.Help;

public static class HelpEndpoints
{
    public static IEndpointRouteBuilder MapHelpEndpoints(this IEndpointRouteBuilder app)
    {
        // Any authenticated role — including Sponsor. Per-role audience filtering happens
        // inside the handlers (see ListHelpTopicsHandler). Reads only for now — writes
        // (POST/PUT/DELETE, AdministratorOrAbove) land in later PR slices (Phase 2/3).
        var group = app.MapGroup("/help").WithTags("Help").RequireAuthorization();

        group.MapGet("/topics", async (ClaimsPrincipal principal, IMediator mediator, CancellationToken ct) =>
                (await mediator.Send(new ListHelpTopicsQuery(principal), ct)).ToHttpResult())
            .WithName("ListHelpTopics");

        return app;
    }
}
