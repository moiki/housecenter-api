using System.Security.Claims;
using HouseCenter.Api.Domain.Constants;
using HouseCenter.Api.Domain.Entities;

namespace HouseCenter.Api.Features.Help;

// Shared audience-ceiling ladder used by ListHelpTopicsHandler AND GetHelpTopicHandler.
// An inclusive floor per proposal.md, NOT the same enum/constants as PolicyNames/RoleNames
// (see the comment on HelpAudience itself).
// Sponsor -> All; Doctor -> +Staff; Member -> +MemberOrAbove;
// Administrator -> +AdministratorOrAbove; Owner -> everything.
internal static class HelpAudienceLadder
{
    private static readonly IReadOnlyDictionary<string, HelpAudience> RoleCeilings =
        new Dictionary<string, HelpAudience>
        {
            [RoleNames.Sponsor] = HelpAudience.All,
            [RoleNames.Doctor] = HelpAudience.Staff,
            [RoleNames.Member] = HelpAudience.MemberOrAbove,
            [RoleNames.Administrator] = HelpAudience.AdministratorOrAbove,
            [RoleNames.Owner] = HelpAudience.OwnerOnly
        };

    // Highest ceiling across every role the caller holds (defaults to the most restrictive
    // tier if no known role claim is present, which RequireAuthorization should never allow
    // through anyway).
    public static HelpAudience CeilingFor(ClaimsPrincipal principal) =>
        RoleNames.All
            .Where(principal.IsInRole)
            .Select(role => RoleCeilings.GetValueOrDefault(role, HelpAudience.All))
            .DefaultIfEmpty(HelpAudience.All)
            .Max();
}
