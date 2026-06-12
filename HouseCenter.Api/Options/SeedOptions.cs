namespace HouseCenter.Api.Options;

public class SeedOptions
{
    public const string SectionName = "Seed";

    public string OwnerEmail { get; set; } = string.Empty;
    public string OwnerPassword { get; set; } = string.Empty;
    public string OwnerFirstName { get; set; } = "System";
    public string OwnerLastName { get; set; } = "Owner";
}
