namespace HouseCenter.Api.Options;

public class TranslationOptions
{
    public const string SectionName = "Translation";

    /// <summary>Google Cloud project ID with the Cloud Translation API enabled.</summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>
    /// Google Cloud service-account JSON (same credential shape as <c>Push:CredentialsJson</c> —
    /// Cloud Translation v3 authenticates via OAuth2 service-account credentials, not a bare API
    /// key). When <see cref="ProjectId"/> is blank, translation is "not configured": the app
    /// wires a logging no-op client instead of calling Google (same dev-friendly fallback as
    /// Push/SMTP with no credentials). Set both via User Secrets / Secret Manager when the GCP
    /// project exists.
    /// </summary>
    public string CredentialsJson { get; set; } = string.Empty;
}
