namespace HouseCenter.Api.Domain.Entities;

// Content-visibility floor for a HelpTopic row — an inclusive ladder, NOT a route policy
// gate. This is a NEW enum, independent from Domain.Constants.PolicyNames/RoleNames:
// HelpAudience decides which audience CAN SEE a given guide topic, while PolicyNames
// decides which role CAN CALL a given endpoint. Changing one does not affect the other.
// If a 6th role is ever added, update RoleNames, the AuthorizationBuilder policies in
// Program.cs, AND this ladder together — there is no shared source of truth between them.
public enum HelpAudience
{
    All,
    Staff,
    MemberOrAbove,
    AdministratorOrAbove,
    OwnerOnly
}
