namespace HouseCenter.Api.Domain.Constants;

public static class PolicyNames
{
    /// <summary>Owner only — system-level operations.</summary>
    public const string OwnerOnly = "OwnerOnly";

    /// <summary>Administrator or Owner — day-to-day management.</summary>
    public const string AdministratorOrAbove = "AdministratorOrAbove";

    /// <summary>Member, Administrator, or Owner — field operations.</summary>
    public const string MemberOrAbove = "MemberOrAbove";

    /// <summary>Every role except Sponsor — sponsors only see aggregate /reports.</summary>
    public const string StaffOnly = "StaffOnly";
}
