using System.Security.Claims;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.Help;

public sealed record GetHelpTopicQuery(string TopicKey, ClaimsPrincipal Principal)
    : IRequest<Result<HelpTopicResponse>>;

public sealed class GetHelpTopicHandler(HouseCenterDbContext db)
    : IRequestHandler<GetHelpTopicQuery, Result<HelpTopicResponse>>
{
    // R3: 404, never 403 — both "doesn't exist" and "exists but outside my audience" return
    // the SAME NotFound so a caller can never distinguish the two (existence is not leaked).
    private static readonly Error NotFound = Error.NotFound("help.topic_not_found", "Help topic not found");

    public async Task<Result<HelpTopicResponse>> Handle(GetHelpTopicQuery request, CancellationToken ct)
    {
        // Project the raw Audience alongside the response so the audience-gate below runs
        // in-memory on the materialized enum value, never as a translated SQL "<=" (Audience
        // is stored via HasConversion<string>(), which would compare names alphabetically).
        var projected = await db.HelpTopics
            .AsNoTracking()
            .Where(t => t.TopicKey == request.TopicKey)
            .Select(t => new
            {
                t.Audience,
                Response = new HelpTopicResponse(
                    t.Id, t.TopicKey, t.Title, t.Summary, t.Body,
                    t.Audience.ToString(), t.Category, t.DisplayOrder, t.UpdatedDate)
            })
            .SingleOrDefaultAsync(ct);

        if (projected is null) return NotFound;

        var ceiling = HelpAudienceLadder.CeilingFor(request.Principal);
        return projected.Audience <= ceiling ? projected.Response : NotFound;
    }
}
