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

    /// <summary>Revokes the caller's active session for one device — POST /auth/logout.
    /// Idempotent: a no-op if no active session exists for that device (mirrors
    /// RemovePushSubscription's idempotent-delete-of-own-resource convention).</summary>
    Task RevokeDeviceAsync(Guid userId, string deviceId, CancellationToken ct);

    /// <summary>Lists the caller's own active sessions — GET /auth/sessions. AsNoTracking +
    /// inline projection; MUST NEVER include CurrentTokenHash/PreviousTokenHash (R9).</summary>
    Task<IReadOnlyList<SessionResponse>> GetActiveSessionsAsync(Guid userId, CancellationToken ct);

    /// <summary>Revokes one session by id, strictly scoped to the caller —
    /// DELETE /auth/sessions/{id}. A foreign or nonexistent id returns NotFound, never
    /// Forbidden (anti-enumeration, R7) — mirrors MarkNotificationReadHandler's pattern.</summary>
    Task<Result> RevokeSessionByIdAsync(Guid userId, Guid sessionId, CancellationToken ct);

    /// <summary>Revokes every active session belonging to the caller —
    /// POST /auth/sessions/revoke-all (R7).</summary>
    Task RevokeAllAsync(Guid userId, CancellationToken ct);

    /// <summary>Bulk-revokes every active session for the user as part of a password reset.
    /// Called only by AuthService.ResetPasswordAsync, never a direct endpoint (R8) — the
    /// explicit, auditable replacement for the old lazy `prv`-comparison-kills-refresh
    /// behavior (opaque sessions are no longer tied to `prv` at all).</summary>
    Task RevokeAllForPasswordResetAsync(Guid userId, CancellationToken ct);
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

    public async Task RevokeDeviceAsync(Guid userId, string deviceId, CancellationToken ct)
    {
        // Global soft-delete filter already scopes this to active rows — no explicit
        // IsActive check needed (CLAUDE.md gotcha: that guard would be dead code).
        var session = await db.UserSessions
            .SingleOrDefaultAsync(s => s.UserId == userId && s.DeviceId == deviceId, ct);

        if (session is not null)
            await RevokeAsync(session, RevokedReason.UserLogout, timeProvider.GetUtcNow().UtcDateTime, ct);
    }

    public async Task<IReadOnlyList<SessionResponse>> GetActiveSessionsAsync(Guid userId, CancellationToken ct) =>
        await db.UserSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .Select(s => new SessionResponse(
                s.Id, s.DeviceId, s.DeviceName, s.Platform, s.LastUsedAt, s.CreatedDate))
            .ToListAsync(ct);

    public async Task<Result> RevokeSessionByIdAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        // Scoped to (Id, UserId) — a foreign or nonexistent id returns NotFound, never
        // Forbidden (anti-enumeration, R7; same pattern as MarkNotificationReadHandler).
        var session = await db.UserSessions
            .SingleOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);

        if (session is null)
            return Error.NotFound("sessions.not_found", "Session not found");

        await RevokeAsync(session, RevokedReason.UserRevoked, timeProvider.GetUtcNow().UtcDateTime, ct);
        return Result.Success();
    }

    public Task RevokeAllAsync(Guid userId, CancellationToken ct) =>
        RevokeAllActiveSessionsAsync(userId, RevokedReason.UserRevoked, ct);

    public Task RevokeAllForPasswordResetAsync(Guid userId, CancellationToken ct) =>
        RevokeAllActiveSessionsAsync(userId, RevokedReason.PasswordReset, ct);

    private async Task RevokeAllActiveSessionsAsync(Guid userId, RevokedReason reason, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // Tracked read + loop (not ExecuteUpdateAsync): keeps this unit-testable against EF
        // InMemory (which doesn't support bulk ExecuteUpdate/ExecuteDelete translation) and
        // matches this file's existing tracked-mutate-then-SaveChanges style.
        var sessions = await db.UserSessions.Where(s => s.UserId == userId).ToListAsync(ct);
        if (sessions.Count == 0)
            return;

        foreach (var session in sessions)
        {
            session.IsActive = false;
            session.RevokedAt = now;
            session.RevokedReason = reason;
        }

        await db.SaveChangesAsync(ct);
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
