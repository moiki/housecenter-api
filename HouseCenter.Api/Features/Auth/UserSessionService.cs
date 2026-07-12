using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Entities;
using HouseCenter.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HouseCenter.Api.Features.Auth;

// Owns all UserSession mechanics (create/upsert on login, single-use rotation with grace
// window, reuse/deviceId-mismatch detection). AuthService orchestrates this + still mints
// the access JWT via the untouched ITokenService (design.md: Auth stays Service, not CQRS).
public interface IUserSessionService
{
    /// <summary>Login/signup upsert by (UserId, DeviceId): revokes any existing active
    /// session for that device, inserts a fresh row, returns the raw (unhashed) refresh
    /// token — the only place it ever exists outside the client (design.md decision #5).</summary>
    Task<string> CreateOrRotateOnLoginAsync(
        User user, string deviceId, string? deviceName, DevicePlatform platform, CancellationToken ct);

    /// <summary>Single-use rotation: validates the presented token against Current/Previous,
    /// enforces the grace window + deviceId binding + sliding/absolute TTL, and either
    /// rotates (returning the user + a new raw token) or revokes the session and fails.</summary>
    Task<Result<(User User, string RefreshToken)>> RotateAsync(
        string rawRefreshToken, string deviceId, CancellationToken ct);
}

public sealed class UserSessionService(
    HouseCenterDbContext db,
    TimeProvider timeProvider,
    IRefreshTokenGenerator tokenGenerator,
    ITokenHasher tokenHasher,
    IOptions<UserSessionOptions> sessionOptions,
    ILogger<UserSessionService> logger) : IUserSessionService
{
    // Same anti-enumeration spirit as AuthService.InvalidCredentials: every rotation failure
    // (untraceable token, device mismatch, expiry, reuse) returns this single generic error —
    // never reveal which specific guard tripped.
    private static readonly Error InvalidRefreshToken =
        Error.Unauthorized("auth.invalid_refresh_token", "Refresh token is invalid or expired");

    private readonly UserSessionOptions _options = sessionOptions.Value;

    public async Task<string> CreateOrRotateOnLoginAsync(
        User user, string deviceId, string? deviceName, DevicePlatform platform, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // Upsert by (UserId, DeviceId) — mirrors RegisterPushSubscription's device-keyed
        // upsert (design.md decision #5). Tracked read: we mutate it below.
        var existing = await db.UserSessions
            .SingleOrDefaultAsync(s => s.UserId == user.Id && s.DeviceId == deviceId, ct);

        if (existing is not null)
        {
            existing.IsActive = false;
            existing.RevokedAt = now;
            existing.RevokedReason = RevokedReason.UserRevoked;
        }

        var rawToken = tokenGenerator.Generate();
        var absoluteExpiresAt = now.AddDays(_options.RefreshAbsoluteExpiryDays);

        db.UserSessions.Add(new UserSession
        {
            UserId = user.Id,
            DeviceId = deviceId,
            DeviceName = deviceName,
            Platform = platform,
            CurrentTokenHash = tokenHasher.Hash(rawToken),
            LastUsedAt = now,
            AbsoluteExpiresAt = absoluteExpiresAt,
            ExpiresAt = ClampExpiry(now, absoluteExpiresAt)
        });

        await db.SaveChangesAsync(ct);

        return rawToken;
    }

    public async Task<Result<(User User, string RefreshToken)>> RotateAsync(
        string rawRefreshToken, string deviceId, CancellationToken ct)
    {
        var hash = tokenHasher.Hash(rawRefreshToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // Tracked lookup (write path — no AsNoTracking), including User.Roles for minting
        // the access token afterwards. DbContext is not thread-safe: this is the single
        // sequential query on the one scoped context (design.md rotation algorithm).
        var session = await db.UserSessions
            .Include(s => s.User)
            .ThenInclude(u => u.Roles)
            .SingleOrDefaultAsync(s => s.CurrentTokenHash == hash || s.PreviousTokenHash == hash, ct);

        // Guard 1: no matching session at all — untraceable, nothing to revoke.
        if (session is null)
            return InvalidRefreshToken;

        // Guard 2: the owning user was soft-deleted — the global query filter applied
        // through Include() leaves the navigation null (design.md rotation algorithm #2).
        if (session.User is null)
            return InvalidRefreshToken;

        // Guard 3: deviceId mismatch — wrong device presenting a token, revoke immediately.
        if (session.DeviceId != deviceId)
        {
            await RevokeAsync(session, RevokedReason.ReuseDetected, now, ct);
            LogSecurityEvent("device_id_mismatch", session);
            return InvalidRefreshToken;
        }

        // Guard 4: session hard-expired — single clamped ExpiresAt enforces both the
        // sliding idle window and the absolute cap (design.md decision #4).
        if (now >= session.ExpiresAt)
        {
            await RevokeAsync(session, RevokedReason.ExpiredAbsolute, now, ct);
            return InvalidRefreshToken;
        }

        // Guard 5: matched Previous, but past the grace deadline — replay after rotation.
        var matchedPrevious = session.PreviousTokenHash == hash;
        if (matchedPrevious && (session.PreviousTokenExpiresAt is null || now > session.PreviousTokenExpiresAt))
        {
            await RevokeAsync(session, RevokedReason.ReuseDetected, now, ct);
            LogSecurityEvent("reuse_after_grace", session);
            return InvalidRefreshToken;
        }

        // Guard 6 (else): Current match, or in-window Previous match — rotate.
        var newRawToken = tokenGenerator.Generate();
        session.PreviousTokenHash = session.CurrentTokenHash;
        session.PreviousTokenExpiresAt = now.AddSeconds(_options.RefreshGraceWindowSeconds);
        session.CurrentTokenHash = tokenHasher.Hash(newRawToken);
        session.LastUsedAt = now;
        session.ExpiresAt = ClampExpiry(now, session.AbsoluteExpiresAt);

        // Atomicity: commit before returning — never hand back a token that isn't the
        // committed Current (design.md "Atomicity" note; documented residual race accepted).
        await db.SaveChangesAsync(ct);

        return (session.User, newRawToken);
    }

    // ExpiresAt = min(now + sliding, absolute) — one check (`now < ExpiresAt`) enforces
    // both the idle window and the hard cap (design.md decision #4).
    private DateTime ClampExpiry(DateTime now, DateTime absoluteExpiresAt)
    {
        var slidingExpiresAt = now.AddDays(_options.RefreshSlidingExpiryDays);
        return slidingExpiresAt < absoluteExpiresAt ? slidingExpiresAt : absoluteExpiresAt;
    }

    private async Task RevokeAsync(UserSession session, RevokedReason reason, DateTime now, CancellationToken ct)
    {
        session.IsActive = false;
        session.RevokedAt = now;
        session.RevokedReason = reason;
        await db.SaveChangesAsync(ct);
    }

    private void LogSecurityEvent(string eventType, UserSession session) =>
        logger.LogWarning(
            "Refresh token security event {EventType}: session {SessionId} for user {UserId} device {DeviceId} revoked",
            eventType, session.Id, session.UserId, session.DeviceId);
}
