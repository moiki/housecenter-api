namespace HouseCenter.Api.Options;

public class CorsOptions
{
    public const string SectionName = "Cors";

    // Single named policy applied to the whole pipeline (see Program.cs).
    public const string PolicyName = "WebApp";

    // Allowed browser origins for the React web app. Empty = none (deny by default).
    public string[] AllowedOrigins { get; set; } = [];
}