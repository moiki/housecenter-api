namespace HouseCenter.Api.Options;

public class AppOptions
{
    public const string SectionName = "App";

    public string FrontendUrl { get; set; } = string.Empty;
    public string Environment { get; set; } = "Development";
}
