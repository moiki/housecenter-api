using System.Security.Claims;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Constants;
using HouseCenter.Api.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.Help;

public sealed record ListHelpTopicsQuery(ClaimsPrincipal Principal) : IRequest<Result<List<HelpTopicSummaryResponse>>>;

public sealed class ListHelpTopicsHandler(HouseCenterDbContext db)
    : IRequestHandler<ListHelpTopicsQuery, Result<List<HelpTopicSummaryResponse>>>
{
    // Audience ladder ceiling per role — an inclusive floor per proposal.md, NOT the same
    // enum/constants as PolicyNames/RoleNames (see the comment on HelpAudience itself).
    // Sponsor -> All; Doctor -> +Staff; Member -> +MemberOrAbove;
    // Administrator -> +AdministratorOrAbove; Owner -> everything.
    private static readonly IReadOnlyDictionary<string, HelpAudience> RoleCeilings =
        new Dictionary<string, HelpAudience>
        {
            [RoleNames.Sponsor] = HelpAudience.All,
            [RoleNames.Doctor] = HelpAudience.Staff,
            [RoleNames.Member] = HelpAudience.MemberOrAbove,
            [RoleNames.Administrator] = HelpAudience.AdministratorOrAbove,
            [RoleNames.Owner] = HelpAudience.OwnerOnly
        };

    public async Task<Result<List<HelpTopicSummaryResponse>>> Handle(
        ListHelpTopicsQuery request, CancellationToken ct)
    {
        // Highest ceiling across every role the caller holds (defaults to the most
        // restrictive tier if no known role claim is present, which RequireAuthorization
        // should never allow through anyway).
        var ceiling = RoleNames.All
            .Where(request.Principal.IsInRole)
            .Select(role => RoleCeilings.GetValueOrDefault(role, HelpAudience.All))
            .DefaultIfEmpty(HelpAudience.All)
            .Max();

        // Materialize the allowed set in-memory (not a SQL "<=") — Audience is stored as a
        // string via HasConversion<string>(), so a translated "<=" would compare the enum
        // names ALPHABETICALLY in SQL, not by declaration order. IN-set membership sidesteps
        // that gotcha entirely: only membership matters, not any provider-side ordering.
        var allowedAudiences = Enum.GetValues<HelpAudience>()
            .Where(audience => audience <= ceiling)
            .ToArray();

        return await db.HelpTopics
            .AsNoTracking()
            .Where(t => allowedAudiences.Contains(t.Audience))
            .OrderBy(t => t.Category).ThenBy(t => t.DisplayOrder)
            .Select(t => new HelpTopicSummaryResponse(
                t.TopicKey, t.Title, t.Summary, t.Category, t.DisplayOrder))
            .ToListAsync(ct);
    }
}
