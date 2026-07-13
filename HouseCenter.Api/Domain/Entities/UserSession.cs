namespace HouseCenter.Api.Domain.Entities;

// Deliberately parallel to (not shared with) Notifications' PushPlatform — slice isolation.
// Same 3 values today by coincidence, not because the two concepts are the same thing.
public enum DevicePlatform
{
    Android,
    iOS,
    Web
}

public enum RevokedReason
{
    UserLogout,
    UserRevoked,
    ReuseDetected,
    PasswordReset,
    ExpiredAbsolute
}

// NOTE: unrelated to AttentionSession (a care-visit record in the Patients domain).
// UserSession is a device-bound refresh-token session for authentication — same base
// class, completely different concept. Do not confuse the two when searching "*Session*".
//
// Revocation is modeled as IsActive=false (picked up by BaseEntity's global soft-delete
// filter for free) plus an audit pair (RevokedAt/RevokedReason) — see design.md decision #2.
public class UserSession : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public string DeviceId { get; set; } = string.Empty;
    public string? DeviceName { get; set; }
    public DevicePlatform Platform { get; set; }

    // SHA-256 hex hash of the currently-valid opaque refresh token. Filtered-unique
    // (active rows only) — see UserSessionConfiguration.
    public string CurrentTokenHash { get; set; } = string.Empty;

    // Just-superseded hash, retained until PreviousTokenExpiresAt for reuse detection
    // within the grace window (design.md decision #4 / R4).
    public string? PreviousTokenHash { get; set; }
    public DateTime? PreviousTokenExpiresAt { get; set; }

    public DateTime LastUsedAt { get; set; }

    // Clamped min(now + RefreshSlidingExpiryDays, AbsoluteExpiresAt) — recomputed on
    // every successful refresh (design.md decision #4).
    public DateTime ExpiresAt { get; set; }

    // Set once at creation (now + RefreshAbsoluteExpiryDays), never extended.
    public DateTime AbsoluteExpiresAt { get; set; }

    public DateTime? RevokedAt { get; set; }
    public RevokedReason? RevokedReason { get; set; }
}
