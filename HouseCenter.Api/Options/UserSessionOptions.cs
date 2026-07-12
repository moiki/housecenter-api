namespace HouseCenter.Api.Options;

public class UserSessionOptions
{
    public const string SectionName = "UserSession";

    /// <summary>Idle window: a session's ExpiresAt slides forward this many days on every
    /// successful refresh, capped by AbsoluteExpiresAt (see design.md decision #4).</summary>
    public int RefreshSlidingExpiryDays { get; set; } = 60;

    /// <summary>Hard ceiling from creation — never extended by any refresh.</summary>
    public int RefreshAbsoluteExpiryDays { get; set; } = 180;

    /// <summary>Grace window (seconds) during which a just-superseded (Previous) refresh
    /// token is still accepted — covers lost-response retries without caching plaintext.</summary>
    public int RefreshGraceWindowSeconds { get; set; } = 30;
}
