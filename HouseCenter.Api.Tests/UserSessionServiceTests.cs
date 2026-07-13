using FluentAssertions;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Entities;
using HouseCenter.Api.Features.Auth;
using HouseCenter.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace HouseCenter.Api.Tests;

// Unit tests for UserSessionService — the rotation + grace-window algorithm (design.md).
// EF InMemory (per CLAUDE.md gotcha: EF async methods need a real query provider, plain
// List<T>.AsQueryable() doesn't work) + FakeTimeProvider (the 60d/180d TTL logic is
// untestable against wall-clock time — design.md decision #6). No HTTP, no
// WebApplicationFactory — full HTTP-level coverage lands in PR4 per tasks.md.
public sealed class UserSessionServiceTests : IAsyncLifetime, IDisposable
{
    private readonly HouseCenterDbContext _db;
    private readonly FakeTimeProvider _time;
    private readonly UserSessionOptions _options = new();
    private readonly IUserSessionService _sut;
    private readonly User _user = new()
    {
        FirstName = "Test",
        LastName = "User",
        Email = "session-test@test.local",
        HashedPassword = "irrelevant-for-these-tests",
        Address = "N/A"
    };

    // Second user — used by the PR3 session-management tests to prove self-scoping
    // (list/revoke-device/revoke-by-id/revoke-all/password-reset-revoke must never touch
    // another user's sessions).
    private readonly User _otherUser = new()
    {
        FirstName = "Other",
        LastName = "User",
        Email = "session-test-other@test.local",
        HashedPassword = "irrelevant-for-these-tests",
        Address = "N/A"
    };

    public UserSessionServiceTests()
    {
        var dbOptions = new DbContextOptionsBuilder<HouseCenterDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()) // unique DB per test
            .Options;
        _db = new HouseCenterDbContext(dbOptions);

        _time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        _sut = new UserSessionService(
            _db,
            _time,
            new RefreshTokenGenerator(),
            new TokenHasher(),
            Microsoft.Extensions.Options.Options.Create(_options),
            NullLogger<UserSessionService>.Instance);
    }

    public async Task InitializeAsync()
    {
        _db.Users.AddRange(_user, _otherUser);
        await _db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _db.Dispose();

    // ─── Rotation — Current token match ──────────────────────────────────────

    [Fact]
    public async Task Rotate_WithCurrentTokenMatch_ReturnsNewTokenDifferentFromOld()
    {
        var t1 = await _sut.CreateOrRotateOnLoginAsync(_user, "device-A", "Phone", DevicePlatform.Android, CancellationToken.None);

        var result = await _sut.RotateAsync(t1, "device-A", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RefreshToken.Should().NotBe(t1);
        result.Value.User.Id.Should().Be(_user.Id);

        var session = await _db.UserSessions.SingleAsync(s => s.UserId == _user.Id && s.DeviceId == "device-A");
        session.PreviousTokenHash.Should().NotBeNull();
    }

    // ─── Grace window ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Rotate_WithinGraceWindow_PreviousTokenIsAccepted()
    {
        var t1 = await _sut.CreateOrRotateOnLoginAsync(_user, "device-A", null, DevicePlatform.Web, CancellationToken.None);
        var firstRotation = await _sut.RotateAsync(t1, "device-A", CancellationToken.None);
        firstRotation.IsSuccess.Should().BeTrue();
        var t2 = firstRotation.Value.RefreshToken;

        // Advance within the grace window (default 30s) and retry with the just-superseded t1
        // — covers a lost-response retry without caching plaintext (design.md decision #2/#4).
        _time.Advance(TimeSpan.FromSeconds(_options.RefreshGraceWindowSeconds - 1));

        var graceResult = await _sut.RotateAsync(t1, "device-A", CancellationToken.None);

        graceResult.IsSuccess.Should().BeTrue();
        graceResult.Value.RefreshToken.Should().NotBe(t1);
        graceResult.Value.RefreshToken.Should().NotBe(t2);
    }

    [Fact]
    public async Task Rotate_OneSecondPastGraceDeadline_TreatedAsReuse_RevokesSessionAndInvalidatesCurrentToo()
    {
        var t1 = await _sut.CreateOrRotateOnLoginAsync(_user, "device-A", null, DevicePlatform.Web, CancellationToken.None);
        var firstRotation = await _sut.RotateAsync(t1, "device-A", CancellationToken.None); // t1 -> t2; t1 becomes Previous
        var t2 = firstRotation.Value.RefreshToken;

        _time.Advance(TimeSpan.FromSeconds(_options.RefreshGraceWindowSeconds + 1));

        var result = await _sut.RotateAsync(t1, "device-A", CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);

        var session = await _db.UserSessions.IgnoreQueryFilters()
            .SingleAsync(s => s.UserId == _user.Id && s.DeviceId == "device-A");
        session.IsActive.Should().BeFalse();
        session.RevokedReason.Should().Be(RevokedReason.ReuseDetected);

        // R5: revocation invalidates both Current and Previous immediately — t2 (the token
        // that WAS still valid) must now also fail.
        var t2Result = await _sut.RotateAsync(t2, "device-A", CancellationToken.None);
        t2Result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Rotate_ReuseWellAfterGraceWindow_RevokesSession()
    {
        var t1 = await _sut.CreateOrRotateOnLoginAsync(_user, "device-A", null, DevicePlatform.Web, CancellationToken.None);
        await _sut.RotateAsync(t1, "device-A", CancellationToken.None);

        // Far past the grace deadline — a classic stolen-token replay, not an off-by-one edge case.
        _time.Advance(TimeSpan.FromHours(1));

        var result = await _sut.RotateAsync(t1, "device-A", CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);

        var session = await _db.UserSessions.IgnoreQueryFilters()
            .SingleAsync(s => s.UserId == _user.Id && s.DeviceId == "device-A");
        session.IsActive.Should().BeFalse();
        session.RevokedReason.Should().Be(RevokedReason.ReuseDetected);
    }

    // ─── No match at all (garbage / access token) — nothing to revoke ────────

    [Fact]
    public async Task Rotate_TokenMatchingNoSession_ReturnsUnauthorizedWithoutRevokingAnything()
    {
        await _sut.CreateOrRotateOnLoginAsync(_user, "device-A", null, DevicePlatform.Web, CancellationToken.None);

        var result = await _sut.RotateAsync("garbage-token-matching-nothing", "device-A", CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);

        // Untraceable token — the existing session must remain untouched (nothing to revoke).
        var session = await _db.UserSessions.SingleAsync(s => s.UserId == _user.Id && s.DeviceId == "device-A");
        session.IsActive.Should().BeTrue();
        session.RevokedReason.Should().BeNull();
    }

    // ─── DeviceId mismatch ─────────────────────────────────────────────────────

    [Fact]
    public async Task Rotate_DeviceIdMismatch_RevokesSessionAndReturnsUnauthorized()
    {
        var t1 = await _sut.CreateOrRotateOnLoginAsync(_user, "device-A", null, DevicePlatform.Web, CancellationToken.None);

        var result = await _sut.RotateAsync(t1, "device-B", CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);

        var session = await _db.UserSessions.IgnoreQueryFilters()
            .SingleAsync(s => s.UserId == _user.Id && s.DeviceId == "device-A");
        session.IsActive.Should().BeFalse();
        session.RevokedReason.Should().Be(RevokedReason.ReuseDetected);

        // Even the correct deviceId can no longer use this token — the session is fully revoked.
        var retry = await _sut.RotateAsync(t1, "device-A", CancellationToken.None);
        retry.IsFailure.Should().BeTrue();
    }

    // ─── Sliding / absolute TTL ─────────────────────────────────────────────────

    [Fact]
    public async Task Rotate_WithinSlidingWindow_ExtendsExpiresAtForward()
    {
        var t1 = await _sut.CreateOrRotateOnLoginAsync(_user, "device-A", null, DevicePlatform.Web, CancellationToken.None);
        var initialExpiresAt = (await _db.UserSessions.AsNoTracking()
            .SingleAsync(s => s.UserId == _user.Id && s.DeviceId == "device-A")).ExpiresAt;

        _time.Advance(TimeSpan.FromDays(30)); // within the 60d window, nowhere near the 180d cap

        var rotated = await _sut.RotateAsync(t1, "device-A", CancellationToken.None);
        rotated.IsSuccess.Should().BeTrue();

        var refreshed = await _db.UserSessions.AsNoTracking()
            .SingleAsync(s => s.UserId == _user.Id && s.DeviceId == "device-A");
        refreshed.ExpiresAt.Should().BeAfter(initialExpiresAt);
        refreshed.ExpiresAt.Should().BeBefore(refreshed.AbsoluteExpiresAt);
    }

    [Fact]
    public async Task Rotate_RepeatedWithinSlidingWindow_ClampsExpiresAtToAbsoluteCapNearEndOfLife()
    {
        var token = await _sut.CreateOrRotateOnLoginAsync(_user, "device-A", null, DevicePlatform.Web, CancellationToken.None);

        // Three refreshes spaced 50 days apart — each is within the 60d sliding window, so each
        // succeeds and pushes ExpiresAt forward, until sliding would exceed the 180d absolute cap.
        for (var i = 0; i < 3; i++)
        {
            _time.Advance(TimeSpan.FromDays(50));
            var rotated = await _sut.RotateAsync(token, "device-A", CancellationToken.None);
            rotated.IsSuccess.Should().BeTrue();
            token = rotated.Value.RefreshToken;
        }

        // 150 days elapsed: unclamped sliding would be 150+60=210d, past the 180d absolute cap.
        var session = await _db.UserSessions.AsNoTracking()
            .SingleAsync(s => s.UserId == _user.Id && s.DeviceId == "device-A");
        session.ExpiresAt.Should().Be(session.AbsoluteExpiresAt);
        session.ExpiresAt.Should().BeBefore(session.LastUsedAt.AddDays(_options.RefreshSlidingExpiryDays));
    }

    [Fact]
    public async Task Rotate_PastAbsoluteCap_ReturnsUnauthorizedAndRevokesEvenAfterValidRotations()
    {
        var token = await _sut.CreateOrRotateOnLoginAsync(_user, "device-A", null, DevicePlatform.Web, CancellationToken.None);

        for (var i = 0; i < 3; i++)
        {
            _time.Advance(TimeSpan.FromDays(50)); // day 50, 100, 150 — each within the sliding window
            var rotated = await _sut.RotateAsync(token, "device-A", CancellationToken.None);
            rotated.IsSuccess.Should().BeTrue();
            token = rotated.Value.RefreshToken;
        }

        // Day 181 — past the 180d absolute cap set once at creation, never extended.
        _time.Advance(TimeSpan.FromDays(31));

        var result = await _sut.RotateAsync(token, "device-A", CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);

        var session = await _db.UserSessions.IgnoreQueryFilters()
            .SingleAsync(s => s.UserId == _user.Id && s.DeviceId == "device-A");
        session.IsActive.Should().BeFalse();
        session.RevokedReason.Should().Be(RevokedReason.ExpiredAbsolute);
    }

    // ─── Login/signup upsert ────────────────────────────────────────────────────

    [Fact]
    public async Task CreateOrRotateOnLogin_SecondLoginSameDevice_SupersedesPriorActiveSession()
    {
        var t1 = await _sut.CreateOrRotateOnLoginAsync(_user, "device-A", "First Login", DevicePlatform.Web, CancellationToken.None);

        var t2 = await _sut.CreateOrRotateOnLoginAsync(_user, "device-A", "Second Login", DevicePlatform.iOS, CancellationToken.None);

        t2.Should().NotBe(t1);

        var allSessionsForDevice = await _db.UserSessions.IgnoreQueryFilters()
            .Where(s => s.UserId == _user.Id && s.DeviceId == "device-A")
            .ToListAsync();
        allSessionsForDevice.Should().HaveCount(2);
        allSessionsForDevice.Count(s => s.IsActive).Should().Be(1);
        allSessionsForDevice.Single(s => !s.IsActive).RevokedReason.Should().Be(RevokedReason.UserRevoked);
        allSessionsForDevice.Single(s => s.IsActive).Platform.Should().Be(DevicePlatform.iOS);

        // The superseded token must no longer work; the fresh one must.
        var oldTokenResult = await _sut.RotateAsync(t1, "device-A", CancellationToken.None);
        oldTokenResult.IsFailure.Should().BeTrue();

        var newTokenResult = await _sut.RotateAsync(t2, "device-A", CancellationToken.None);
        newTokenResult.IsSuccess.Should().BeTrue();
    }

    // ─── PR3: session management + reset (R7, R8, R9) ──────────────────────────

    [Fact]
    public async Task GetActiveSessions_ReturnsOnlyCallersSessions_NeverAnotherUsers()
    {
        await _sut.CreateOrRotateOnLoginAsync(_user, "device-A", "My Phone", DevicePlatform.Android, CancellationToken.None);
        await _sut.CreateOrRotateOnLoginAsync(_user, "device-B", "My Laptop", DevicePlatform.Web, CancellationToken.None);
        await _sut.CreateOrRotateOnLoginAsync(_otherUser, "device-C", "Their Phone", DevicePlatform.iOS, CancellationToken.None);

        var sessions = await _sut.GetActiveSessionsAsync(_user.Id, CancellationToken.None);

        sessions.Should().HaveCount(2);
        sessions.Select(s => s.DeviceId).Should().BeEquivalentTo(["device-A", "device-B"]);
    }

    [Fact]
    public async Task RevokeDevice_RevokesOnlyThatUserAndDeviceSession_OtherSessionsUnaffected()
    {
        var deviceAToken = await _sut.CreateOrRotateOnLoginAsync(_user, "device-A", null, DevicePlatform.Android, CancellationToken.None);
        var deviceBToken = await _sut.CreateOrRotateOnLoginAsync(_user, "device-B", null, DevicePlatform.Web, CancellationToken.None);
        var otherUserToken = await _sut.CreateOrRotateOnLoginAsync(_otherUser, "device-A", null, DevicePlatform.iOS, CancellationToken.None);

        await _sut.RevokeDeviceAsync(_user.Id, "device-A", CancellationToken.None);

        var revokedSession = await _db.UserSessions.IgnoreQueryFilters()
            .SingleAsync(s => s.UserId == _user.Id && s.DeviceId == "device-A");
        revokedSession.IsActive.Should().BeFalse();
        revokedSession.RevokedReason.Should().Be(RevokedReason.UserLogout);

        // The caller's other device is untouched.
        (await _sut.RotateAsync(deviceBToken, "device-B", CancellationToken.None)).IsSuccess.Should().BeTrue();

        // Same deviceId for a DIFFERENT user is a completely separate row — untouched.
        (await _sut.RotateAsync(otherUserToken, "device-A", CancellationToken.None)).IsSuccess.Should().BeTrue();

        // The revoked device's token no longer works.
        (await _sut.RotateAsync(deviceAToken, "device-A", CancellationToken.None)).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task RevokeDevice_NoActiveSessionForThatDevice_IsANoOp()
    {
        // Idempotent: logging out a device that has no active session must not throw
        // and must not touch any other session.
        var otherDeviceToken = await _sut.CreateOrRotateOnLoginAsync(_user, "device-B", null, DevicePlatform.Web, CancellationToken.None);

        await _sut.RevokeDeviceAsync(_user.Id, "device-never-logged-in", CancellationToken.None);

        (await _sut.RotateAsync(otherDeviceToken, "device-B", CancellationToken.None)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task RevokeAll_RevokesEveryActiveSessionForCaller_OtherUsersSessionsUnaffected()
    {
        await _sut.CreateOrRotateOnLoginAsync(_user, "device-A", null, DevicePlatform.Android, CancellationToken.None);
        await _sut.CreateOrRotateOnLoginAsync(_user, "device-B", null, DevicePlatform.Web, CancellationToken.None);
        var otherUserToken = await _sut.CreateOrRotateOnLoginAsync(_otherUser, "device-C", null, DevicePlatform.iOS, CancellationToken.None);

        await _sut.RevokeAllAsync(_user.Id, CancellationToken.None);

        var callerSessions = await _db.UserSessions.IgnoreQueryFilters()
            .Where(s => s.UserId == _user.Id).ToListAsync();
        callerSessions.Should().HaveCount(2);
        callerSessions.Should().OnlyContain(s => !s.IsActive && s.RevokedReason == RevokedReason.UserRevoked);

        // The other user is completely unaffected.
        (await _sut.RotateAsync(otherUserToken, "device-C", CancellationToken.None)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task RevokeSessionById_OwnedSession_RevokesAndReturnsSuccess()
    {
        await _sut.CreateOrRotateOnLoginAsync(_user, "device-A", null, DevicePlatform.Android, CancellationToken.None);
        var sessionId = (await _db.UserSessions.AsNoTracking()
            .SingleAsync(s => s.UserId == _user.Id && s.DeviceId == "device-A")).Id;

        var result = await _sut.RevokeSessionByIdAsync(_user.Id, sessionId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var session = await _db.UserSessions.IgnoreQueryFilters().SingleAsync(s => s.Id == sessionId);
        session.IsActive.Should().BeFalse();
        session.RevokedReason.Should().Be(RevokedReason.UserRevoked);
    }

    [Fact]
    public async Task RevokeSessionById_ForeignId_IsANoOpAndReturnsNotFound_NeverForbidden()
    {
        // Anti-enumeration (R7): a session belonging to another user must be reported as
        // NotFound (so the endpoint maps it to 404, never 403) and must be left untouched.
        await _sut.CreateOrRotateOnLoginAsync(_user, "device-A", null, DevicePlatform.Android, CancellationToken.None);
        var otherUserToken = await _sut.CreateOrRotateOnLoginAsync(_otherUser, "device-C", null, DevicePlatform.iOS, CancellationToken.None);
        var otherSessionId = (await _db.UserSessions.AsNoTracking()
            .SingleAsync(s => s.UserId == _otherUser.Id && s.DeviceId == "device-C")).Id;

        var result = await _sut.RevokeSessionByIdAsync(_user.Id, otherSessionId, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.NotFound);

        // The foreign session is completely untouched.
        (await _sut.RotateAsync(otherUserToken, "device-C", CancellationToken.None)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task RevokeSessionById_NonexistentId_ReturnsNotFound()
    {
        var result = await _sut.RevokeSessionByIdAsync(_user.Id, Guid.NewGuid(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task RevokeAllForPasswordReset_RevokesEveryDeviceForThatUser_WithPasswordResetReason()
    {
        var deviceAToken = await _sut.CreateOrRotateOnLoginAsync(_user, "device-A", null, DevicePlatform.Android, CancellationToken.None);
        var deviceBToken = await _sut.CreateOrRotateOnLoginAsync(_user, "device-B", null, DevicePlatform.Web, CancellationToken.None);
        var otherUserToken = await _sut.CreateOrRotateOnLoginAsync(_otherUser, "device-C", null, DevicePlatform.iOS, CancellationToken.None);

        await _sut.RevokeAllForPasswordResetAsync(_user.Id, CancellationToken.None);

        var callerSessions = await _db.UserSessions.IgnoreQueryFilters()
            .Where(s => s.UserId == _user.Id).ToListAsync();
        callerSessions.Should().HaveCount(2);
        callerSessions.Should().OnlyContain(s => !s.IsActive && s.RevokedReason == RevokedReason.PasswordReset);

        // Both of the caller's devices are dead post-reset (R8).
        (await _sut.RotateAsync(deviceAToken, "device-A", CancellationToken.None)).IsFailure.Should().BeTrue();
        (await _sut.RotateAsync(deviceBToken, "device-B", CancellationToken.None)).IsFailure.Should().BeTrue();

        // A password reset for one user must never touch another user's sessions.
        (await _sut.RotateAsync(otherUserToken, "device-C", CancellationToken.None)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task RevokeAllForPasswordReset_NoActiveSessions_IsANoOpAndDoesNotThrow()
    {
        var act = () => _sut.RevokeAllForPasswordResetAsync(_user.Id, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
