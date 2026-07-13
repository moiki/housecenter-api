# SDD Design — Device-Bound Sessions

## Change name
`device-bound-sessions`

## Status
`design` (2026-07-11)

## Technical approach

Move refresh authority out of the self-contained JWT and into a persisted, per-device
`UserSession` row. The `Refresh` `TokenKind` becomes an opaque 256-bit random string whose
SHA-256 hash is the DB lookup key; `Access`/`Invitation`/`PasswordReset` stay JWTs. A new
scoped `IUserSessionService` owns all session mechanics (create/upsert, rotate, revoke,
list); `AuthService` orchestrates it and still mints the access JWT via the untouched
`ITokenService`. `TimeProvider` is introduced (singleton) so the 60d/180d TTL logic is
deterministically testable. Auth stays the **Service** pattern — no CQRS.

---

## Architecture decisions

| # | Decision | Choice | Rejected alternative | Rationale |
|---|---|---|---|---|
| 1 | Token shape | **Opaque** random 256-bit token (base64url), SHA-256 hash as DB key | JWT-with-`sid`/`jti` (mobile plan's ambiguous phrasing) | Authority now lives in the row; a JWT still requires a DB hit for rotation/revocation state, so the signature is pure overhead. Avoids `exp`/`ClockSkew` drift vs the DB's own `ExpiresAt`. `TokenPairResponse` wire shape unchanged — clients don't know it's opaque. |
| 2 | Rotation lineage | **Current + Previous hash pair on the same row** | Separate lineage/family table | Reuse detection only ever asks "does this hash match Current, or the immediately-previous?" — there is no chain to walk. Keeps the model as simple as `PushSubscription`'s single-row-per-device shape. |
| 3 | Hash algorithm | **SHA-256 (fast hash)** | BCrypt | Correct *because* the token is high-entropy random data, not a low-entropy human secret. BCrypt's work factor exists to slow brute force of guessable passwords — pointless and costly per-refresh here. Contrast `User.HashedPassword` (BCrypt, correctly). A code comment on `ITokenHasher`/`UserSessionOptions` MUST state this so nobody "fixes" it to BCrypt by false analogy. |
| 4 | TTL modeling | **One clamped `ExpiresAt = min(now + 60d, AbsoluteExpiresAt)`** | Two separate checks (sliding + absolute) at refresh | A single `now < ExpiresAt` check enforces both the sliding idle window and the hard 180d ceiling. `AbsoluteExpiresAt` is set once, never extended. |
| 5 | Login/signup persistence | **Upsert by `(UserId, DeviceId)`** — revoke the existing active row, insert fresh | Always insert (accumulate rows) | Mirrors `RegisterPushSubscription`'s device-keyed upsert: prevents stale rows from reinstalls/logout-without-`/auth/logout`, and cleanly handles a different user logging into a shared device. |
| 6 | Time source | **`AddSingleton(TimeProvider.System)`**, injected into `UserSessionService` only | Keep `DateTime.UtcNow` everywhere | 60d/180d TTLs are untestable against wall-clock time; `FakeTimeProvider` is the standard driver. Scoped narrowly to the session path — the DbContext audit hook keeps `DateTime.UtcNow` (no codebase-wide refactor). |
| 7 | `JwtOptions.RefreshSecret` | **Delete it** (4 touch points) | Leave it validated-but-unused | Once Refresh is not a JWT the secret is dead; a still-validated dead secret is a smell and ".NET secrets in appsettings" is a flagged anti-pattern. |
| 8 | `TokenKind` migration | **Partial — remove only `Refresh`** from the enum + `TokenService` | Migrate all kinds / keep a dead `Refresh` member | Contained blast radius: drop `CreateRefreshToken`, the `Refresh` case in `SigningKeyFor`, and the `token_kind=Refresh` path. `Access`/`Invitation`/`PasswordReset` JWT flows and the lazy `JwtBearerOptions` wiring are untouched. |

**Documented residual race (not hidden):** two concurrent refreshes of the same Current
token (or an attacker racing a legitimate retry inside the ~30s grace window) can both mint
valid pairs before either is flagged — the losing new token lands outside `Previous` and is
reuse-detected on its next use. This is Auth0's accepted reuse-with-grace tradeoff; mitigation
is the small window. An optional Postgres `xmin` concurrency token would harden it but is out
of scope per the proposal.

---

## Data model

`UserSession : BaseEntity` — inherits `Id / IsActive / CreatedDate / UpdatedDate`. "Revoked"
= `IsActive = false` (picked up by the global soft-delete filter for free) + an audit pair.

| Field | Type | Notes |
|---|---|---|
| `UserId` | `Guid` (FK → `User`, cascade) | |
| `DeviceId` | `string` (required) | client-generated UUID |
| `DeviceName` | `string?` | display-only |
| `Platform` | `DevicePlatform` enum | `.HasConversion<string>()` |
| `CurrentTokenHash` | `string` (64) | SHA-256 hex — **filtered-unique index** (active only) |
| `PreviousTokenHash` | `string?` (64) | just-superseded hash; retained past grace for reuse detection |
| `PreviousTokenExpiresAt` | `DateTime?` | grace deadline for `PreviousTokenHash` |
| `LastUsedAt` | `DateTime` | bumped every successful refresh |
| `ExpiresAt` | `DateTime` | clamped `min(now + 60d, AbsoluteExpiresAt)` |
| `AbsoluteExpiresAt` | `DateTime` | `now + 180d` at creation, never extended |
| `RevokedAt` | `DateTime?` | audit |
| `RevokedReason` | `RevokedReason?` enum | `.HasConversion<string>()` |

**New enums** (`Domain/Entities`): `DevicePlatform { Android, iOS, Web }` — deliberately
parallel to but independent from `PushPlatform`, with a comment noting the intentional
symmetry (slice isolation). `RevokedReason { UserLogout, UserRevoked, ReuseDetected,
PasswordReset, ExpiredAbsolute }`. `User` gains `ICollection<UserSession> Sessions`.

`UserSessionConfiguration : IEntityTypeConfiguration<UserSession>` (template:
`PushSubscriptionConfiguration`):

```csharp
builder.Property(s => s.DeviceId).IsRequired().HasMaxLength(64);
builder.Property(s => s.CurrentTokenHash).IsRequired().HasMaxLength(64);
builder.Property(s => s.PreviousTokenHash).HasMaxLength(64);
builder.Property(s => s.Platform).HasConversion<string>().HasMaxLength(10);
builder.Property(s => s.RevokedReason).HasConversion<string>().HasMaxLength(20);

// Uniqueness scoped to ACTIVE rows: revoked rows keep their hash for audit,
// so a global unique index would be semantically wrong (SHA-256 collisions aside).
builder.HasIndex(s => s.CurrentTokenHash).IsUnique().HasFilter("\"IsActive\" = true");
builder.HasIndex(s => s.PreviousTokenHash);          // reuse lookup
builder.HasIndex(s => s.UserId);                      // GET /auth/sessions
builder.HasIndex(s => new { s.UserId, s.DeviceId });  // login-time upsert

builder.HasOne(s => s.User).WithMany(u => u.Sessions)
    .HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
```

`DbSet<UserSession> UserSessions` on `HouseCenterDbContext`. All `DateTime` fields get the
global UTC `ValueConverter` and the `IsActive` filter **for free** — no per-property code.
Single self-contained migration `AddUserSessions` (generated `*.Designer.cs` excluded from
the review line budget).

---

## Sequence diagrams

**(a) Login / signup → create-or-upsert session**
```
Client                 AuthService            UserSessionService         DB
  | POST /login (creds, deviceId, platform)   |                          |
  |----------------------->|                   |                          |
  |                        | verify BCrypt     |                          |
  |                        |------------------>| CreateOrRotate(user,dev) |
  |                        |                   | find active (UserId,Dev) |
  |                        |                   |------------------------->|
  |                        |                   | revoke old if present    |
  |                        |                   | gen T, insert row        |
  |                        |                   | Current=hash(T)          |
  |                        |                   | Abs=now+180d Exp=clamp    |
  |                        |                   | SaveChanges ------------->|
  |                        |<-- raw T ---------|                          |
  |                        | CreateAccessToken(user) [JWT, untouched]     |
  |<-- TokenPair(access,T)-|                   |                          |
```

**(b) Refresh — Current match (normal rotation)**
```
POST /refresh (T, deviceId) -> hash=SHA256(T)
  find active session WHERE CurrentTokenHash=hash OR PreviousTokenHash=hash
  matched Current, deviceId ok, now<ExpiresAt:
    Previous = Current ; PreviousExpiresAt = now + grace(30s)
    Current  = hash(T') ; LastUsedAt = now ; ExpiresAt = clamp(now)
    SaveChanges  ---(commit BEFORE responding)--->  200 TokenPair(access, T')
```

**(c) Refresh within grace — Previous match**
```
matched PreviousTokenHash=hash AND now <= PreviousTokenExpiresAt:
  identical rotate-and-respond path as (b)  -> 200 TokenPair(access, T'')
  (survives lost-response retries without caching plaintext)
```

**(d) Reuse / deviceId mismatch → revoke session**
```
matched Previous but now > PreviousTokenExpiresAt   (replay after grace)
   OR session.DeviceId != request.deviceId          (wrong device)
   OR now >= ExpiresAt                              (expired / abs cap):
     IsActive=false ; RevokedReason=ReuseDetected|ExpiredAbsolute ; RevokedAt=now
     Serilog security event (structured)
     SaveChanges  --->  401
no row matched at all -> 401 (no traceable session, nothing to revoke)
```

**(e) Password reset → bulk revoke**
```
ResetPasswordAsync: validate PasswordReset JWT (prv, unchanged)
  user.HashedPassword = BCrypt(new) ; user.PasswordRecoveryVersion++   (prv path intact)
  userSessions.RevokeAllForPasswordReset(userId):
     UPDATE all active UserSession of user -> IsActive=false, RevokedReason=PasswordReset
  SaveChanges  (immediate + auditable, replaces today's lazy prv-comparison for Refresh)
```

---

## Rotation + grace-window algorithm

Single tracked lookup (write path — **no** `AsNoTracking`), including `User.Roles` for
minting the access token afterwards:

```
hash = hasher.Hash(request.RefreshToken)
now  = timeProvider.GetUtcNow().UtcDateTime
session = UserSessions.Include(User).ThenInclude(Roles)
           .FirstOrDefault(CurrentTokenHash == hash || PreviousTokenHash == hash)
```

Branch order (guards before rotate):
1. `session is null` → 401 (untraceable).
2. `session.User is null` (soft-deleted user, filtered out by the Include) → 401.
3. `session.DeviceId != request.DeviceId` → revoke(`ReuseDetected`) → 401.
4. `now >= session.ExpiresAt` → revoke(`ExpiredAbsolute`) → 401 (covers idle-60d and abs-180d).
5. matched **Previous** AND `now > PreviousTokenExpiresAt` → revoke(`ReuseDetected`) → 401.
6. otherwise (Current match, or in-window Previous) → **rotate**: shift `Previous=Current`,
   `PreviousExpiresAt=now+grace`, `Current=hash(newToken)`, `LastUsedAt=now`,
   `ExpiresAt=clamp(now)`, `SaveChangesAsync`, then return the pair.

**Atomicity:** `SaveChangesAsync` (rotation write, or the revoke write) MUST complete before
the response is returned — never hand a client a token that is not the committed `Current`,
never return 401 for reuse without the revocation persisted. The read-then-write on one row
is the documented residual race (above); the grace window bounds its impact.

---

## Service / handler structure

Auth = **Service** pattern (per module table), not CQRS. New scoped
`IUserSessionService` encapsulates all session mechanics and is the single place the DbContext
is mutated for sessions:

```
IUserSessionService
  Task<string> CreateOrRotateOnLoginAsync(User, DeviceContext, ct)   // returns raw refresh token
  Task<Result<(User user, string refreshToken)>> RotateAsync(rawToken, deviceId, ct)
  Task RevokeDeviceAsync(userId, deviceId, ct)                        // POST /auth/logout
  Task<IReadOnlyList<SessionResponse>> ListAsync(userId, ct)          // GET /auth/sessions
  Task<Result> RevokeByIdAsync(userId, sessionId, ct)                 // DELETE /auth/sessions/{id}
  Task RevokeAllAsync(userId, ct)                                     // revoke-all
  Task RevokeAllForPasswordResetAsync(userId, ct)                     // called by ResetPasswordAsync
```

`AuthService.LoginAsync/SignupAsync/RefreshAsync/ResetPasswordAsync` delegate to it and keep
minting the **access** JWT via `ITokenService` (JWT path fully unchanged). Two small stateless
singletons back the token itself:

```
IRefreshTokenGenerator : string Generate()   // RandomNumberGenerator.GetBytes(32) -> base64url
ITokenHasher           : string Hash(string) // Convert.ToHexString(SHA256.HashData(...)).ToLower()
                                              // COMMENT: fast hash is correct for high-entropy tokens — do NOT switch to BCrypt
```

New `Options/UserSessionOptions.cs` (`RefreshSlidingExpiryDays=60`,
`RefreshAbsoluteExpiryDays=180`, `RefreshGraceWindowSeconds=30`) keeps `JwtOptions`
JWT-signing-only. Endpoints wire into `AuthEndpoints.MapAuthEndpoints` following the
`/auth/me` shape: bare `.RequireAuthorization()`, `userId` from `ClaimTypes.NameIdentifier`,
strictly self-scoped, `NotFound` (not `Forbidden`) for foreign ids.

**Gotchas honored:** `GET /auth/sessions` uses `AsNoTracking()` + **inline** `new
SessionResponse(...)` projection (never a method ref) and never projects a hash field; the
rotate path awaits its single query sequentially on the one scoped DbContext (no
`Task.WhenAll` — DbContext is not thread-safe). New DTOs: `deviceId` (required),
`deviceName?`, `platform` added to `LoginRequest`/`RefreshRequest`/`SignupRequest` +
`LogoutRequest(deviceId)` + `SessionResponse`; `TokenPairResponse` unchanged; matching
FluentValidation validators for the new required fields.

---

## TimeProvider integration

- `builder.Services.AddSingleton(TimeProvider.System)` in `Program.cs`.
- `UserSessionService` takes `TimeProvider` and derives `now =
  timeProvider.GetUtcNow().UtcDateTime` for `ExpiresAt`, `AbsoluteExpiresAt`, `LastUsedAt`,
  `PreviousTokenExpiresAt`, `RevokedAt`. `AbsoluteExpiresAt` is computed from `now + 180d`
  (**not** `CreatedDate + 180d`) so `FakeTimeProvider` drives it; `CreatedDate` stays on the
  DbContext audit hook's `DateTime.UtcNow` (minor, intentional discrepancy — audit only).
- **Test wiring without regressing lazy JwtBearer:** in `ApiFactory.ConfigureTestServices`,
  swap the singleton exactly like `IEmailService`/`IPushSender` already are —
  `services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(Time);` — and
  expose `public FakeTimeProvider Time { get; }`. This is an orthogonal DI swap; it does
  **not** touch `AddOptions<JwtBearerOptions>().Configure<IOptions<JwtOptions>>()`, so the
  documented WebApplicationFactory lazy-config gotcha is preserved.
- **Boundary:** `FakeTimeProvider` governs only session TTL logic. JwtBearer access-token
  expiry uses its own internal `DateTime.UtcNow` and is intentionally *not* driven by it — so
  advancing the fake clock 60/180 days must exercise `/auth/refresh` (session path), not
  bearer-protected endpoints.
- **Shared-fixture caution:** `ApiFactory` is a `[Collection("Api")]` fixture; a mutated
  `FakeTimeProvider` bleeds across tests. TTL/cap tests SHOULD use a dedicated `ApiFactory`
  instance (own client) rather than the shared collection to avoid clock cross-talk.
- New **test-only** package: `Microsoft.Extensions.TimeProvider.Testing`. `SHA256` /
  `RandomNumberGenerator` are in-box (no package).

---

## Security considerations

- **Hashing:** SHA-256 fast hash for high-entropy tokens (decision 3); raw token never stored.
- **Revocation completeness:** all four endpoints self-scoped via `ClaimTypes.NameIdentifier`;
  `GET /auth/sessions` projects only `Id, DeviceId, DeviceName, Platform, LastUsedAt,
  CreatedDate` — never a hash. Foreign/unknown id → `NotFound` (anti-enumeration).
- **Absolute cap:** `AbsoluteExpiresAt` never extended; the clamp guarantees a single
  `now < ExpiresAt` enforces both windows — closes today's unbounded-lifetime gap.
- **`prv` interplay — net improvement:** `prv`/JWT path for `PasswordReset` unchanged;
  `ResetPasswordAsync` now *also* immediately bulk-revokes all `UserSession` rows — auditable,
  strictly stronger than today's lazy comparison.
- **Blast-radius reduction:** leaked token auto-revoked on first replay (vs valid up to 7d
  today); one device revocable without logging out others (vs global `prv` bump only).
- **Cutover tradeoff:** no legacy stateless-JWT dual-accept path — one-time forced re-login
  accepted, avoiding a second security-critical branch. Deliberate.

---

## Rollback

- **Pre-cutover:** revert PRs; `dotnet ef migrations remove` drops `AddUserSessions`. No data
  to preserve.
- **Post-cutover:** the old build reintroduces stateless refresh JWTs, so **`RefreshSecret`
  must be restored in config before redeploying it** (deleted in PR2) — note in the deploy
  runbook. The `UserSession` table can be left in place (harmless to the old build) or dropped
  via down-migration. Clients hit a one-time forced re-login, symmetric with the forward
  cutover — same accepted, bounded cost. No runtime feature-flag toggle (rejected — it is the
  dual-path complexity Open Question 4 declines).

---

## Open questions

- [ ] `RevokedReason` label for idle-60d sliding expiry funnels through `ExpiredAbsolute` (the
  enum has no separate `ExpiredIdle`). Acceptable as-is, or add a member? (minor — defer to tasks)
