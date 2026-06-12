namespace HouseCenter.Api.Domain.Constants;

public static class RoleNames
{
    public const string Owner = "Owner";
    public const string Administrator = "Administrator";
    public const string Member = "Member";
    public const string Doctor = "Doctor";
    public const string Sponsor = "Sponsor";

    public static readonly string[] All = [Owner, Administrator, Member, Doctor, Sponsor];
}
