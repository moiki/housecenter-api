namespace HouseCenter.Api.Options;

public class PushOptions
{
    public const string SectionName = "Push";

    /// <summary>
    /// Firebase service-account JSON. When blank, push is "not configured": the app wires a
    /// logging no-op sender instead of FCM (same dev-friendly fallback as SMTP with no host).
    /// Set this via User Secrets / Secret Manager when the Firebase project exists.
    /// </summary>
    public string CredentialsJson { get; set; } = string.Empty;
}
