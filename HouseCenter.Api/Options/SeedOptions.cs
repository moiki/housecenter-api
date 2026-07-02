namespace HouseCenter.Api.Options;

public class SeedOptions
{
    public const string SectionName = "Seed";

    public string OwnerEmail { get; set; } = string.Empty;
    public string OwnerPassword { get; set; } = string.Empty;
    public string OwnerFirstName { get; set; } = "System";
    public string OwnerLastName { get; set; } = "Owner";

    /// <summary>Password for every user created by POST /seed (Development/Staging test data).</summary>
    public string TestUserPassword { get; set; } = "Demo@123!";
}
