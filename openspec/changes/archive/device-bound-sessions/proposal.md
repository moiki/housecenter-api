# SDD Proposal — Device-Bound Sessions

## Change name
`device-bound-sessions`

## Status
`proposed` (2026-07-11)

## Problem

Refresh tokens in `HouseCenter.Api` are stateless 7-day HS256 JWTs with **zero
persistence** and **zero per-device control**. Three concrete gaps follow directly from
that (all confirmed against the live code in exploration):

- **No rotation, no revocation on refresh.** `AuthService.RefreshAsync` (`AuthService.cs:46-67`)
  validates the presented JWT's signature + `token_kind` claim, then mints a brand-new pair
  via `IssueTokenPair` with **no reference to the token presented**. The old refresh token
  stays independently valid until its own 7-day `exp` — a leaked token is usable for up to a
  week in parallel with the legitimate one, invisibly.
- **No per-device / per-session lever.** The only revocation mechanism is the
  `PasswordRecoveryVersion` / `prv` claim, and it is all-or-nothing: bumping it logs out
  **every** device at once. There is no "log out this phone," no "list my active sessions,"
  no "log out everywhere but here."
- **Unbounded lifetime.** There is **no absolute cap** today. A token refreshed every few
  days lives forever. The 7-day `exp` only bounds an *idle* token.

For an NGO with a small internal staff base this is tolerable today, but it is the #1 item
on the mobile roadmap and a prerequisite for shipping the mobile app: a stolen or lost
phone must be revocable individually, and a leaked token must not survive a week undetected.

## Proposed change

Move refresh-token authority **out of the self-contained JWT and into a persisted
`UserSession` database row**, one row per `(user, device)`:

1. **Opaque, cryptographically-random refresh tokens.** The `Refresh` `TokenKind` stops
   being a JWT and becomes a 256-bit `RandomNumberGenerator` value, base64url-encoded. Its
   **SHA-256 hex hash** is the DB lookup key (unique index). The raw token is never stored.
   `Access`, `Invitation`, and `PasswordReset` stay JWTs — this is a contained migration of
   one kind only. `TokenPairResponse(AccessToken, RefreshToken)` wire shape is unchanged, so
   web/mobile clients don't need to know the refresh token is now opaque.
2. **Single-use rotation + reuse detection + ~30s grace window** (Auth0-style). Each refresh
   rotates: the old hash moves to `PreviousTokenHash` (valid only for a short grace window),
   a fresh token becomes `CurrentTokenHash`. Presenting a token that matches neither Current
   nor an in-window Previous → **revoke the whole session** (`RevokedReason = ReuseDetected`)
   + structured Serilog security event + 401. A `deviceId` mismatch is treated the same.
3. **Persisted, self-service revocation surface** — `POST /auth/logout` (this device),
   `GET /auth/sessions` (list my devices), `DELETE /auth/sessions/{id}` (one device),
   `POST /auth/sessions/revoke-all` (everywhere).
4. **Sliding 60-day / absolute 180-day TTL**, clamped into one `ExpiresAt` field so a single
   `now < ExpiresAt` check enforces both the sliding window and the hard ceiling.
5. **Explicit bulk-revoke on password reset** inside `ResetPasswordAsync` — immediate and
   auditable, strictly stronger than today's lazy "fails next time it's presented" behavior.
   The existing `prv` / JWT path for `TokenKind.PasswordReset` itself is unchanged.

The security win, quantified: a leaked token is auto-revoked on first replay (not valid for
7 days); a single lost device is revocable without logging out the others; and the 180-day
absolute cap closes today's unbounded-lifetime gap entirely.

## Entities affected

### New: `UserSession : BaseEntity`

Inherits `Id / IsActive / CreatedDate / UpdatedDate` from `BaseEntity`. "Revoked" is modeled
as `IsActive = false` (picked up by the global soft-delete filter for free) plus an audit
timestamp/reason pair.

| Field | Type | Notes |
|---|---|---|
| `UserId` | `Guid` (FK → `User`, cascade delete) | |
| `DeviceId` | `string` (required) | client-generated UUID |
| `DeviceName` | `string?` | display-only, e.g. "iPhone 14 — Maria" |
| `Platform` | `DevicePlatform` enum, `.HasConversion<string>()` | Android / iOS / Web |
| `CurrentTokenHash` | `string` (SHA-256 hex, 64) | **unique index** — O(1) refresh lookup |
| `PreviousTokenHash` | `string?` | just-superseded hash, valid only in the grace window |
| `PreviousTokenExpiresAt` | `DateTime?` | grace-window deadline for `PreviousTokenHash` |
| `LastUsedAt` | `DateTime` | bumped on every successful refresh |
| `ExpiresAt` | `DateTime` | sliding cursor, clamped `min(now + 60d, AbsoluteExpiresAt)` |
| `AbsoluteExpiresAt` | `DateTime` | set once at creation = `CreatedDate + 180d`, never extended |
| `RevokedAt` | `DateTime?` | audit trail, alongside `IsActive = false` |
| `RevokedReason` | `RevokedReason?` enum | `UserLogout / UserRevoked / ReuseDetected / PasswordReset / ExpiredAbsolute` |

Indexes: **unique** on `CurrentTokenHash`; non-unique on `PreviousTokenHash`, on `UserId`
(for `GET /auth/sessions`), and on `(UserId, DeviceId)` (for the login-time upsert). No
separate rotation-lineage table — the Current + Previous hash pair on the row is the entire
lineage. Modeled on `PushSubscriptionConfiguration.cs` (unique device-scoped index +
`HasConversion<string>()` enum + cascade to `User`).

### New enums

- `DevicePlatform { Android, iOS, Web }` — deliberately **parallel to but independent from**
  `Features/Notifications`' `PushPlatform` (see Open Question 5). Comment the intentional
  symmetry so a future reader doesn't assume they're the same type.
- `RevokedReason { UserLogout, UserRevoked, ReuseDetected, PasswordReset, ExpiredAbsolute }`.

### Modified

- **`User`** — gains a `ICollection<UserSession> Sessions` navigation collection (FK owner,
  cascade delete).
- **`AuthService`** — `LoginAsync` / `SignupAsync` (persist + upsert a session per device),
  `RefreshAsync` (rotate + reuse-detect + grace window + deviceId check + TTL clamp, replacing
  the stateless mint), `ResetPasswordAsync` (add bulk session revoke). Auth stays the
  **Service** pattern (not CQRS), per the module table.
- **Config** — new `Options/UserSessionOptions.cs` (`RefreshSlidingExpiryDays = 60`,
  `RefreshAbsoluteExpiryDays = 180`, `RefreshGraceWindowSeconds = 30`). `JwtOptions.RefreshSecret`
  is **deleted** (Open Question 1). `TimeProvider.System` registered as a singleton (Open Question 2).
- **DTOs** — `login` / `refresh` / `signup` requests gain `deviceId` (required),
  `deviceName?`, `platform`. `TokenPairResponse` unchanged.
- **`HouseCenterDbContext`** — new `DbSet<UserSession>` + `UserSessionConfiguration`.

## Scope

- New `UserSession : BaseEntity` + `UserSessionConfiguration` + migration + `DbSet` +
  `User.Sessions` navigation.
- New `DevicePlatform` and `RevokedReason` enums.
- Opaque refresh-token generator (`RandomNumberGenerator`, base64url) + SHA-256 hasher.
- `UserSessionOptions` + `TimeProvider.System` singleton registration.
- `AuthService` rewrite of login / refresh / signup for persistence, single-use rotation,
  reuse detection + grace window, deviceId verification, and clamped sliding/absolute TTL.
- Request DTO additions (`deviceId` / `deviceName` / `platform`) on login / refresh / signup.
- New self-service endpoints: `POST /auth/logout`, `GET /auth/sessions`,
  `DELETE /auth/sessions/{id}`, `POST /auth/sessions/revoke-all`.
- Explicit bulk session revoke inside `ResetPasswordAsync`.
- Deletion of `JwtOptions.RefreshSecret` (4 touch points).
- Integration + unit tests (rotation, reuse→revoke, grace accept/boundary, revoke-all,
  per-device revoke, `GET /auth/sessions` scoping, TTL sliding + cap via `FakeTimeProvider`,
  `prv` still kills all).

## Out of scope

- **Legacy stateless-JWT dual-accept path at cutover** — deliberately not built (Open
  Question 4). One-time forced re-login is the accepted deploy cost.
- Access-token / `Access` `TokenKind` behavior — unchanged. The lazy `JwtBearerOptions`
  wiring (`Program.cs:119-131`) is **not** touched.
- `Invitation` and `PasswordReset` token kinds — stay JWTs, unchanged (`prv` guard intact).
- Session activity/geo/IP metadata, "trust this device," or step-up/MFA re-auth.
- Rate-limiting or lockout on `/auth/refresh` (existing infra, separate concern).
- Admin-facing "view/revoke another user's sessions" (self-service only for now).
- Push-notification-token lifecycle (`PushSubscription`) — unrelated, unchanged.

## Access control

All new endpoints follow the `GET /auth/me` shape (`AuthEndpoints.cs:23-29`): bare
`.RequireAuthorization()` (any authenticated user), `userId` from `ClaimTypes.NameIdentifier`,
strictly **self-scoped** — a user can only see/revoke their own sessions. Foreign or
nonexistent ids return **`NotFound`, not `Forbidden`** (anti-enumeration, matching the
codebase convention at `AuthService.cs:31-32` / `AuthEndpoints.cs:36-41`). No `PolicyNames`
policy is needed.

| Endpoint | Auth | Scope | Foreign/unknown id |
|---|---|---|---|
| `POST /auth/logout` | `RequireAuthorization` | `WHERE UserId = caller AND DeviceId = body.deviceId AND IsActive` | no-op / 204 (nothing to reveal) |
| `GET /auth/sessions` | `RequireAuthorization` | caller's active rows only; projects `Id, DeviceId, DeviceName, Platform, LastUsedAt, CreatedDate` — **never** hash fields | n/a |
| `DELETE /auth/sessions/{id}` | `RequireAuthorization` | `WHERE Id = id AND UserId = caller` | `NotFound` |
| `POST /auth/sessions/revoke-all` | `RequireAuthorization` | bulk-revoke all caller's active rows | n/a |

Existing `/auth/refresh` remains anonymous (no bearer yet) but now authenticates by
hash-matching a `UserSession` row and verifying `deviceId` — a stronger check than today's
signature-only validation.

## Open questions (positions taken)

| # | Question | Position | Rationale |
|---|---|---|---|
| 1 | Delete `JwtOptions.RefreshSecret`? (4 touch points: `Program.cs:57-61` validation, `appsettings.json:17`, `ApiFactory.cs:63`, `CLAUDE.md:187`) | **Yes — delete it.** | Once `Refresh` stops being a JWT, the secret is unused. A still-validated but dead secret is a smell; ".NET secrets in appsettings.json" is a flagged anti-pattern. Cleaner than leaving it. Done in PR2 alongside the `RefreshAsync` rewrite. |
| 2 | Introduce `TimeProvider`? (zero usage today; all dates are `DateTime.UtcNow` directly) | **Yes — `AddSingleton(TimeProvider.System)`**, inject into the session issue/refresh path. | The 60d/180d TTL logic is untestable with wall-clock time. `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`) is the standard way to drive it. New pattern for this codebase — flagged for `sdd-design`. Grace-window (~30s) tests can use real short waits; TTL tests cannot. |
| 3 | Grace-window duration? | **30 seconds, config-driven** via `UserSessionOptions.RefreshGraceWindowSeconds`. | Matches the existing `ClockSkew = 30s` precedent (`TokenService.cs:83`) rather than inventing a number. Small window keeps the documented race residual (see below) tight. |
| 4 | Legacy stateless-JWT dual-accept path at cutover? | **No.** Ship as a breaking change for the Refresh path only; accept a one-time forced re-login. | Small internal NGO staff (not public consumers); the existing 7-day `exp` already bounds exposure to under a week with zero extra code; forced re-login is a mild UX bump, not data loss. Avoids a second security-critical code path that would need tests and deliberate removal later. |
| 5 | Reuse `PushPlatform` or new `DevicePlatform` enum? | **New `DevicePlatform` enum.** | Reusing `Features/Notifications`' `PushPlatform` would make `Features/Auth` depend on a type owned by another vertical slice, breaking slice isolation. Deliberately-parallel independent enum, documented (same call the help-center exploration made for `HelpAudience` vs `PolicyNames`). |

**Documented residual risk (stated, not hidden):** with a grace window, an attacker holding
a token copy from the *instant* it is superseded could race a legitimate client's retry
within the ~30s window and both obtain valid rotated pairs before either is flagged. This is
the same accepted tradeoff Auth0's reuse-detection-with-grace design documents; mitigation is
keeping the window small (30s). Accepted deliberately.

## Affected modules / packages

- **`Features/Auth`** — `AuthService.cs`, `AuthEndpoints.cs`, `AuthDtos.cs` (DTO additions +
  new session-list DTO), `TokenService.cs` (`Refresh` kind → opaque generation path).
- **`Domain/Entities`** — new `UserSession.cs`; `User.cs` (add `Sessions` nav); new
  `DevicePlatform` / `RevokedReason` enums.
- **`Data`** — `HouseCenterDbContext.cs` (`DbSet<UserSession>`); new
  `Data/Configurations/UserSessionConfiguration.cs`; new `Migrations/*_AddUserSessions.cs`.
- **`Options`** — new `Options/UserSessionOptions.cs`; `Options/JwtOptions.cs` (remove
  `RefreshSecret`).
- **`Program.cs`** — remove `RefreshSecret` validation; register `TimeProvider.System`;
  bind `UserSessionOptions`; map the 4 new endpoints.
- **Config** — `appsettings.json` (remove `Jwt:RefreshSecret`, add `UserSession` section).
- **`HouseCenter.Api.Tests`** — `ApiFactory.cs` (drop `RefreshSecret` override, add
  `FakeTimeProvider` wiring for TTL tests); extend `LoginFlowTests.cs` / new session tests.
- **New packages** — `Microsoft.Extensions.TimeProvider.Testing` (test project only).
  `System.Security.Cryptography` (`SHA256`, `RandomNumberGenerator`) is in-box, no package.
- **Docs** — `CLAUDE.md` (update the "4 secrets" auth note and the User Secrets list).

## Delivery plan (chained PRs)

Full change is well over the 400-line review budget, so it ships as 4 ordered slices, each
building + testing green on its own. `delivery_strategy: ask-on-risk`. Generated
`*.Designer.cs` migration files are excluded from the line budget.

| PR | Scope | Depends on |
|---|---|---|
| **PR1 — Foundation** | `UserSession : BaseEntity` + `DevicePlatform` / `RevokedReason` enums + `UserSessionConfiguration` + migration + `DbSet` + `User.Sessions` nav + `UserSessionOptions` + opaque-token generator (`RandomNumberGenerator`, base64url) + SHA-256 hasher + `TimeProvider.System` registration. No behavior change to auth flows yet. | — |
| **PR2 — Auth flow rewrite** | `AuthService.LoginAsync` / `RefreshAsync` / `SignupAsync` rewrite (persist, upsert-per-device, single-use rotation, reuse detection + grace window, deviceId check, clamped sliding/absolute TTL) + request DTO changes (`deviceId` / `deviceName` / `platform`) + `AuthEndpoints` wiring + **delete `RefreshSecret`** (all 4 touch points). | PR1 |
| **PR3 — Session management** | `POST /auth/logout`, `GET /auth/sessions`, `DELETE /auth/sessions/{id}`, `POST /auth/sessions/revoke-all` (self-scoped, `NotFound` for foreign ids, no hash fields in the list DTO) + `ResetPasswordAsync` bulk session revoke. | PR2 |
| **PR4 — Tests** | xUnit + WebApplicationFactory: rotation, reuse→revoke, grace accept, grace boundary, revoke-all, per-device revoke, `GET /auth/sessions` scoping, TTL sliding + absolute cap via `FakeTimeProvider`, `prv`-still-kills-all (first integration coverage of `ResetPasswordAsync`). | PR3 |

> Note: this codebase is `strict_tdd: true`, so tests ship *with* the behavior in each PR;
> PR4 is the dedicated **integration/time-driven** surface (`FakeTimeProvider`, multi-device
> WebApplicationFactory flows) that can't be co-located cheaply with PR1–PR3 unit coverage.

## Rollback plan

- **Pre-cutover / not yet deployed:** revert the PR(s); `dotnet ef migrations remove` drops
  the `AddUserSessions` migration. No data to preserve.
- **Post-cutover:** rolling back reintroduces stateless refresh JWTs, so `RefreshSecret` must
  be **restored in config before** redeploying the old build (it was deleted in PR2) — note
  this in the deploy runbook. The `UserSession` table can be left in place (harmless, unused
  by the old build) and dropped later, or dropped via the down-migration if a clean revert is
  wanted. Either way clients hit a one-time forced re-login on rollback, symmetric with the
  one-time re-login on the forward cutover (Open Question 4) — same accepted, bounded cost.
- **Feature-flag alternative (not chosen):** a runtime toggle between stateless and
  DB-backed refresh was considered and rejected — it is exactly the dual-path complexity
  Open Question 4 declines to build. The migration is self-contained and the user base is
  small enough that PR-level revert + forced re-login is the simpler, safer lever.
