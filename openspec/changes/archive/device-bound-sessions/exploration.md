# Exploration — Device-Bound Sessions (replacing stateless refresh tokens)

## Summary

Today `HouseCenter.Api` refresh tokens are stateless 7-day HS256 JWTs with zero persistence,
zero per-device revocation, and only one blunt revocation lever (`PasswordRecoveryVersion`/`prv`,
which kills every device at once). This exploration confirms that claim against the live code and
lands on a concrete design: a new `UserSession` entity (opaque, hashed, single-use-rotated refresh
tokens; a Current+Previous hash pair per row for the reuse/grace-window mechanics — no separate
lineage table needed); persisted revocation surfaced via `/auth/logout`, `/auth/sessions`,
`/auth/sessions/revoke-all`, `DELETE /auth/sessions/{id}`; sliding 60-day / absolute 180-day TTL
clamped into one field; and an explicit, auditable bulk-revoke on password reset that *replaces*
(and strictly improves on) the current lazy `prv`-claim comparison for the Refresh path only.
The broader mobile plan (`~/.claude/plans/hola-analiza-los-proyectos-frolicking-puzzle.md`) is
directionally correct about this change but under-specifies the token mechanics — one phrase in
its own PR sketch ("`TokenService` con `sid`/`jti` y hash SHA-256 del token") is ambiguous between
"still a JWT, but hashed" and "opaque token, hashed" — this exploration resolves that ambiguity
explicitly in favor of **opaque, not JWT**, with rationale below.

**Confidence: high — proceed to `sdd-propose`.** No blocking unknowns; the open items below
(delete `RefreshSecret`?, introduce `TimeProvider`?, exact grace-window seconds) are proposal-level
decisions to confirm, not exploration gaps.

## Confirmed

- **No session/device/revocation surface exists today.** `grep -r "session|device|RefreshToken|revoke|logout"` across `HouseCenter.Api/**/*.cs` (case-insensitive) returns only unrelated hits (`AttentionSession`, `SessionReminderOptions`, `PushSubscription` "device install" comment, migration snapshots) — zero `UserSession`/`DeviceId`/`Revoke*`/`Logout*` symbols anywhere. Confirms the requirement's premise precisely.
- **`AuthEndpoints.cs:9-51`** — current surface is exactly `POST /login`, `POST /refresh`, `GET /me`, `POST /signup`, `POST /password/request`, `POST /password/reset`. No `/logout`, no `/sessions/*`.
- **`AuthService.RefreshAsync` (AuthService.cs:46-67)** — validates the refresh JWT via `tokenService.ValidateAsync(request.RefreshToken, TokenKind.Refresh)` (signature + `KindClaim` check only, `TokenService.cs:76-92`), loads the user, compares `principal.FindFirstValue(TokenService.PasswordVersionClaim)` against `user.PasswordRecoveryVersion` (line 62-64), and on success calls `IssueTokenPair(user)` (line 175-176) which mints a **brand-new** access+refresh pair with no reference to the presented token at all — i.e. **no rotation, no persistence, no revocation of the old token.** The old refresh token remains independently valid (via its own unexpired signature) until its own 7-day `exp` elapses, fully in parallel with the new one. This is the exact gap the requirement describes.
- **`TokenService.cs:11-17,28-118`** — 4 `TokenKind`s (`Access, Refresh, Invitation, PasswordReset`), each with its own secret (`SigningKeyFor`, line 110-117), `KindClaim="token_kind"` (line 31) and `PasswordVersionClaim="prv"` (line 32) as defense-in-depth so no token kind can be reused as another. `ClockSkew = TimeSpan.FromSeconds(30)` (line 83) is the existing precedent for "small time tolerance" — reused below as the grace-window magnitude.
- **`JwtOptions.cs:3-17`** — `RefreshTokenExpiryDays = 7` (no absolute/sliding split, no per-device anything). `appsettings.json:15-26` mirrors it (all secrets blank, filled via User Secrets per `CLAUDE.md:181-193`).
- **`Program.cs:54-64`** validates `Jwt:Secret`/`RefreshSecret`/`InvitationSecret`/`ResetSecret` are each ≥32 bytes and `Secret != RefreshSecret`, via `.ValidateOnStart()` — fail-fast at boot, not first use.
- **`Program.cs:119-131`** — `JwtBearerOptions` configured **lazily** via `AddOptions<JwtBearerOptions>(...).Configure<IOptions<JwtOptions>>(...)`, with an explicit code comment (line 108-112) explaining *why*: an eager snapshot would ignore `WebApplicationFactory`'s test-host config overrides. This must NOT be touched/regressed by this change (only the `Access` kind flows through this path anyway — Refresh moving off JWT rails doesn't touch it).
- **`ResetPasswordAsync` (AuthService.cs:149-173)** — validates a `TokenKind.PasswordReset` JWT (own secret, own `prv` claim check as a one-shot-token guard, line 162-164), then does `user.PasswordRecoveryVersion++` (line 167) with the comment *"invalidates this reset token AND all outstanding refresh tokens"* — today this "invalidation" is entirely lazy/implicit: an already-issued refresh JWT is never touched, it simply fails `RefreshAsync`'s `prv` comparison the next time (if ever) it's presented. Nothing is revoked in the database; there is no database row to revoke.
- **Entity/config conventions, all confirmed against real files:**
  - `BaseEntity` (`Domain/Entities/BaseEntity.cs:3-9`) = `Id, IsActive, CreatedDate, UpdatedDate`.
  - Global soft-delete filter (`Data/HouseCenterDbContext.cs:36-44`) applies automatically to any `BaseEntity` subtype; `.IgnoreQueryFilters()` bypasses it. A new `UserSession : BaseEntity` gets this for free — "revoked" can literally be `IsActive = false` with zero extra filtering code, exactly like every other soft-deleted row in this codebase.
  - Global UTC `DateTime` `ValueConverter` (`HouseCenterDbContext.cs:46-58,62-68`) — new `DateTime`/`DateTime?` properties on `UserSession` (e.g. `ExpiresAt`, `LastUsedAt`) get Npgsql-safe UTC handling with zero per-property code, matching the documented gotcha in `CLAUDE.md:146-151`.
  - `IEntityTypeConfiguration<T>` + Fluent API is the universal pattern; `Data/Configurations/PushSubscriptionConfiguration.cs:7-27` is the closest existing template: `HasIndex(...).IsUnique()` on a device-scoped string value, `.HasConversion<string>()` on a `Platform` enum, cascade delete tied to the owning `User`. This is a near-1:1 template for `UserSessionConfiguration` (unique index on `CurrentTokenHash` instead of `Token`).
  - **`PushSubscription` (`Domain/Entities/PushSubscription.cs:3-20`)** already defines `enum PushPlatform { Android, iOS, Web }` — exactly the platform enum the requirement asks for, but scoped to `Features/Notifications`. Its upsert pattern (`Features/Notifications/RegisterPushSubscription.cs:22-40`: look up by unique device value, update-in-place if found, insert if not, comment explicitly calls out "same device may re-register... or change owner") is the right mental model for "new login on the same `deviceId` should reuse/replace, not pile up, session rows" — see position below on whether to literally reuse `PushPlatform` or clone it.
  - `Common/Results/Error.cs` factories confirmed: `Validation, Unauthorized, Forbidden, NotFound, Conflict, Failure` (lines 15-20) — `Forbidden` exists but the codebase's own convention (line 31-32 `InvalidCredentials`, `AuthEndpoints.cs:36-41` 202-always-on-password-request) is anti-enumeration: prefer `NotFound` over `Forbidden` for "not yours" cases so an attacker probing IDs can't distinguish "doesn't exist" from "belongs to someone else."
  - `Domain/Constants/PolicyNames.cs:3-16` — `OwnerOnly, AdministratorOrAbove, MemberOrAbove, StaffOnly`, wired in `Program.cs:134-138`. New session-management endpoints don't need any of these — they're "any authenticated user manages their own sessions," identical shape to `GET /auth/me` (`AuthEndpoints.cs:23-29`, bare `.RequireAuthorization()`, `userId` from `ClaimTypes.NameIdentifier`).
  - Migration naming precedent (`Migrations/20260710220048_AddWorkRouteRecurrence.cs`, `20260703065226_AddHelpTopics.cs`, etc.) — one self-contained migration per change, landing in the same PR as the entity+config, matching the help-center exploration's own note ("`AddHelpTopics` as a single, self-contained migration... should land in the same PR as T1").
- **Test conventions confirmed:** `HouseCenter.Api.Tests/LoginFlowTests.cs` (WebApplicationFactory + `[Collection("Api")]`) already covers login, `/auth/me`, and a basic refresh round-trip (`Refresh_WithValidRefreshToken_ReturnsNewTokenPair`, lines 71-84) and a cross-kind-rejection test (`Refresh_WithAccessTokenInsteadOfRefreshToken_Returns401`, lines 86-96). `ApiFactory.cs:33-79` injects all 4 Jwt secrets via in-memory config overrides (lines 62-65) and stands up a real `housecenter_test` Postgres DB per run (line 53-56) — this is the harness the new tests plug into. No existing password-reset integration test was found (`grep ResetPassword|prv` in the Tests project returns nothing) — the new "prv still kills all sessions" test will be the first coverage of that path at all, a net-new gain, not just an addition.
- **No `TimeProvider` usage anywhere** (`grep -r "TimeProvider"` returns zero files) — every date in the codebase is `DateTime.UtcNow` directly (`HouseCenterDbContext.cs:83,86`; `TokenService.cs:101`). Relevant because 60-day/180-day TTLs cannot be tested with real time.
- **No `SHA256`/`RandomNumberGenerator` usage anywhere** — hashing/opaque-token generation is genuinely new infrastructure for this change, not an extension of an existing helper.

## Discrepancies / corrections

Checked against the mobile plan (`~/.claude/plans/hola-analiza-los-proyectos-frolicking-puzzle.md`), which frames this change as SDD item #1:

1. **Ambiguous token-shape phrasing in the plan's own PR sketch.** Line 178: *"PR1 entidad `UserSession` + config + migración + `TokenService` con `sid`/`jti` y hash SHA-256 del token (nunca crudo)."* This reads as "keep `TokenService` producing JWTs for Refresh, add a `sid`/`jti` claim, and *also* hash it" — a JWT-plus-hash hybrid. That is a real, coherent alternative design (JWT carries `sid`, DB row keyed by `sid` holds the hash + rotation state), but it's **not what I recommend** — see "Opaque vs JWT" below for the reasoning to go fully opaque instead. Flagging this explicitly so `sdd-propose` states the resolved design rather than inheriting the plan's ambiguous phrasing verbatim.
2. **Entity name `UserSession`** (plan line 178) is confirmed as the right choice and is adopted here — no correction, just confirmation, with one addition: recommend disambiguating in code comments from the unrelated existing `AttentionSession` entity (`Domain/Entities/AttentionSession.cs`, a patient-care-visit record) and `Features/Patients/Sessions/SessionEndpoints.cs` (routes at `/patients/{patientId:guid}/sessions`, confirmed no path collision with the new `/auth/sessions`, `SessionEndpoints.cs:15,33,51`).
3. **Grace window framing matches** (plan line 96, line 229) — no discrepancy, this exploration only adds the concrete mechanics (which token, how long, what "accept" means operationally).
4. **TTL numbers match exactly** (plan line 34: "60d deslizante, tope 180d") — no discrepancy.
5. Everything else in the plan's decision table and requirements list for change #1 (persist per-device, single-use rotation + reuse detection, revoke endpoints, `prv` still works) is directionally accurate and requires no correction — this exploration's job was to turn "requirements" into "mechanics," not to fix errors.

## Additional considerations for the proposal

### 1. Data model — `UserSession`

`UserSession : BaseEntity` (gets `Id/IsActive/CreatedDate/UpdatedDate` for free, per `BaseEntity.cs:3-9`):

| Field | Type | Notes |
|---|---|---|
| `UserId` | `Guid` (FK → `User`, cascade delete) | |
| `DeviceId` | `string` | client-generated UUID, required |
| `DeviceName` | `string?` | e.g. "iPhone 14 — Maria", display-only |
| `Platform` | enum, `.HasConversion<string>()` | Android / iOS / Web |
| `CurrentTokenHash` | `string` (SHA-256 hex, 64 chars) | **unique index** — the O(1) refresh lookup key |
| `PreviousTokenHash` | `string?` | the just-superseded hash, kept only inside the grace window |
| `PreviousTokenExpiresAt` | `DateTime?` | grace-window deadline for `PreviousTokenHash` |
| `LastUsedAt` | `DateTime` | bumped on every successful refresh |
| `ExpiresAt` | `DateTime` | sliding cursor, **clamped**: `min(now + 60d, AbsoluteExpiresAt)` — see below |
| `AbsoluteExpiresAt` | `DateTime` | set once at creation = `CreatedDate + 180d`, never extended |
| `RevokedAt` | `DateTime?` | audit trail, alongside `IsActive=false` |
| `RevokedReason` | enum? | `UserLogout / UserRevoked / ReuseDetected / PasswordReset / ExpiredAbsolute` — for security-log/observability value even though revoked rows are invisible via the global filter |

Indexes: unique on `CurrentTokenHash` (mirrors `PushSubscriptionConfiguration.cs:20`'s `HasIndex(s => s.Token).IsUnique()`), non-unique on `PreviousTokenHash`, non-unique on `UserId` (for `GET /auth/sessions`), non-unique on `(UserId, DeviceId)` (for the login-time upsert lookup below).

**No separate rotation-lineage/family table.** The row IS the lineage: reuse detection only ever needs to ask "does this hash match Current, or a still-valid Previous?" — there is no chain of prior sessions to walk. This keeps the model as simple as `PushSubscription`'s single-row-per-device shape.

**Login-time upsert, not unbounded accumulation.** On `LoginAsync`/`SignupAsync`, look up an existing active `UserSession` for `(UserId, DeviceId)` and revoke it before inserting the fresh row — same reasoning as `RegisterPushSubscription.cs:22-23` ("the same device may re-register... or change owner"): prevents stale rows piling up from app reinstalls or logout-without-hitting-`/auth/logout`, and cleanly handles "a different user logs into a shared device."

**Platform enum: clone, don't reuse `PushPlatform`.** `PushPlatform` (`PushSubscription.cs:3-8`) is already `Android, iOS, Web` — tempting to reuse directly, but doing so would make `Features/Auth` depend on a type owned by `Features/Notifications`, breaking the vertical-slice isolation this codebase otherwise maintains (`Features/` per module, CLAUDE.md architecture table). Recommend a new, deliberately-parallel `Domain/Entities`-level enum (e.g. `DevicePlatform { Android, iOS, Web }`) with a code comment noting the intentional symmetry with `PushPlatform` — same lesson the help-center exploration already flagged for `HelpAudience` vs `PolicyNames` (parallel-but-independent enums, documented so a future reader doesn't assume they're literally the same type).

### 2. Rotation + grace-window mechanics

On `/auth/refresh`: hash the presented token (SHA-256 hex), look up `UserSession` where `IsActive` AND (`CurrentTokenHash == hash` OR (`PreviousTokenHash == hash` AND `now <= PreviousTokenExpiresAt`)).

- **Match on Current** → normal rotation: mint new opaque token `T'`, set `PreviousTokenHash = old CurrentTokenHash`, `PreviousTokenExpiresAt = now + graceSeconds`, `CurrentTokenHash = hash(T')`, bump `LastUsedAt`, recompute clamped `ExpiresAt`. Return the new pair.
- **Match on Previous (within grace window)** → treat identically to a normal rotation (mint yet another new pair, shift the Previous slot forward). This is the Auth0-style "reuse interval" pattern: it is the only way to survive "server committed the rotation but the HTTP response was lost on a flaky rural connection, client retries with the token it still has" *without* caching the plaintext of the previously-minted token (which the "hash only, never raw" requirement forbids). No special-casing needed — every accepted refresh (Current or in-window-Previous) follows the exact same rotate-and-respond code path.
- **No match at all** (neither Current nor a still-valid Previous) → **reuse detected**: this is either a token from ≥2 rotations back (classic theft-then-legitimate-use-then-replay) or a grace-window retry that arrived too late. Revoke the whole session (`IsActive=false`, `RevokedReason=ReuseDetected`), log a structured security event (Serilog, matching existing structured-logging convention), return 401.
- **Residual, documented risk** (state this explicitly in the proposal, don't hide it): if an attacker holds a copy of a token from the *instant* it gets superseded and races a legitimate client's retry within the same few-second grace window, both could obtain valid rotated pairs before either is flagged. This is the same accepted tradeoff Auth0's own reuse-detection-with-grace-period design documents. Mitigation is keeping the window small — recommend **~30 seconds**, matching the existing `ClockSkew` precedent (`TokenService.cs:83`) rather than inventing an unrelated number.
- **Defense-in-depth addition worth flagging:** since `deviceId` is now sent on every refresh too, compare the presented `deviceId` against `session.DeviceId` and treat a mismatch the same as reuse (revoke) — a stolen token presented with the wrong device fingerprint is a meaningful signal, not just noise.

### 3. Opaque tokens vs JWT-with-`sid` — take a position

**Recommendation: fully opaque, cryptographically-random tokens** (e.g. 256-bit `RandomNumberGenerator`, base64url-encoded), **not** JWTs. SHA-256 hash is the sole DB lookup key.

Rationale:
- The entire point of this change is that authority now lives in the **database row**, not in self-contained claims. Once revocation, rotation state, and per-device listing all live in `UserSession`, a JWT wrapper buys nothing — you'd still hit the DB on every refresh to check rotation/revocation state, so the signature-verification step is pure overhead with no remaining security property of its own.
- Avoids an entire class of JWT-specific footguns for this one path: no `exp` claim that could drift from the DB's own `ExpiresAt`/`AbsoluteExpiresAt`, no `ClockSkew` (`TokenService.cs:83`) accidentally forgiving something reuse-detection should catch.
- Net effect on `JwtOptions.RefreshSecret`: it becomes **unused** once Refresh stops being a JWT kind. Recommend **deleting it** (Program.cs:57-61 validation, `appsettings.json:17`, `ApiFactory.cs:63`, `CLAUDE.md:187` secrets doc) rather than leaving an unused, still-validated secret lying around — a stale secret is itself a minor smell, and the .NET rules explicitly flag "secrets in appsettings.json" as an anti-pattern to watch. This is a proposal-level decision to confirm (4 touch points), not something to silently decide in `sdd-tasks`.
- `TokenKind` enum (`TokenService.cs:11-17`) keeps `Access, Invitation, PasswordReset` as JWTs unaffected — this is a partial, well-contained migration of only the `Refresh` kind.
- Wire-compatible: `TokenPairResponse(AccessToken, RefreshToken)` (`AuthDtos.cs:7`) shape is unchanged — `RefreshToken` is just an opaque string instead of a JWT string. Web/mobile clients don't need to know or care.
- Recommend a **new `Options/UserSessionOptions.cs`** (mirrors the existing `SessionReminderOptions.cs` precedent of one small options file per concern) for `RefreshSlidingExpiryDays=60`, `RefreshAbsoluteExpiryDays=180`, `RefreshGraceWindowSeconds=30` — keeps `JwtOptions` scoped to pure JWT-signing config now that Refresh's lifecycle is DB-driven, not JWT-driven.

### 4. Backward compatibility / migration

Existing web clients hold up to 7-day-old stateless refresh JWTs at cutover time (`RefreshTokenExpiryDays=7`, `JwtOptions.cs:14`). Post-deploy, `RefreshAsync` looks up a `UserSession` row by hash — a legacy JWT has no matching row and will 401.

**Recommendation: no dual-accept/legacy-JWT grace path.** Accept the one-time "everyone re-logs-in once" cost rather than building a temporary legacy-token-acceptance branch into the security-critical `RefreshAsync` (which would need a second code path, tests for it, and deliberate removal a week later). Justification: (a) small internal NGO staff user base, not public consumers; (b) `RefreshTokenExpiryDays=7` already bounds the exposure window to under a week with zero extra code; (c) a forced re-login is a mild UX bump, not data loss or a security incident — exactly the kind of throwaway complexity this change is trying to remove from the refresh path, not add back in. State this explicitly as an accepted, deliberate trade-off in the proposal ("ship as a breaking change for Refresh; access tokens and everything else unaffected").

### 5. Security review

- **Hashing choice justified:** SHA-256 (fast hash) is correct here specifically *because* the opaque token is high-entropy random data, not a low-entropy human secret — contrast with `User.HashedPassword` (`User.cs:13`) correctly using BCrypt for passwords. Worth a one-line comment in the config class so a future reviewer doesn't "fix" this into BCrypt by mistaken analogy to password hashing.
- **Revocation completeness:** all four new endpoints sit behind bare `.RequireAuthorization()` (same shape as `/auth/me`, `AuthEndpoints.cs:23-29`) with `userId` from `ClaimTypes.NameIdentifier`. `POST /auth/logout` takes `deviceId` in the body (not the refresh token — cleaner client contract) and revokes `WHERE UserId = caller AND DeviceId = body.deviceId AND IsActive`. `POST /auth/sessions/revoke-all` bulk-revokes all active rows for the caller. `GET /auth/sessions` projects only `Id, DeviceId, DeviceName, Platform, LastUsedAt, CreatedDate` — **never** `CurrentTokenHash`/`PreviousTokenHash` in any DTO. `DELETE /auth/sessions/{id}` scopes the query to `WHERE Id = id AND UserId = caller` and returns `NotFound` (not `Forbidden`) on a foreign/nonexistent id — consistent with this codebase's existing anti-enumeration posture (`AuthService.cs:31-32`, `AuthEndpoints.cs:36-41`).
- **Absolute cap:** `AbsoluteExpiresAt` set once at creation, never extended; sliding `ExpiresAt` is always clamped to `min(now + 60d, AbsoluteExpiresAt)` so a single field check (`now < ExpiresAt`) on refresh correctly enforces both the sliding window and the hard ceiling — no dual-check needed.
- **`prv` interplay — net improvement, not just preservation:** keep `prv` exactly as-is for `TokenKind.PasswordReset` (unchanged JWT flow). For Refresh, `ResetPasswordAsync` (`AuthService.cs:167`) should *also* explicitly bulk-revoke every `UserSession` row for that user the moment the password changes — immediate and auditable, versus today's lazy "unusable next time it's presented, if ever" behavior. This is a genuine strengthening of the requirement ("password reset must still kill all sessions"), worth stating as a selling point in the proposal, not just a constraint being preserved.
- **Blast-radius reduction vs today, quantified:** today a leaked stateless refresh token is valid up to 7 days with zero visibility and only a global `prv` bump as a remedy (kills *every* device, not just the compromised one) — and critically, there is **no absolute cap at all** today: a token refreshed every few days lives forever. Under the new model: (1) a specific leaked token/device can be revoked individually without logging out other devices; (2) reuse detection auto-revokes on first theft-driven double-use without waiting for a human; (3) the 180-day absolute cap bounds even a perfectly-maintained session, closing today's unbounded-lifetime gap entirely.

### 6. Test surface (xUnit + WebApplicationFactory, extending `LoginFlowTests.cs`/`ApiFactory.cs`)

- **Rotation:** refresh returns a token different from the one presented (strengthen the existing `Refresh_WithValidRefreshToken_ReturnsNewTokenPair`, `LoginFlowTests.cs:71-84`, which currently doesn't assert `refreshed.RefreshToken != tokens.RefreshToken`).
- **Reuse → revoke:** rotate T1→T2, advance past the grace window, present T1 again → 401, and confirm T2 (or any other token from that session) *also* now fails — proves the whole session was revoked, not just the one token.
- **Grace window accept:** present the just-rotated-away previous token *within* the window → 200 with a valid new pair.
- **Grace window boundary:** present it *after* the deadline → 401.
- **Revoke-all:** log in on 2+ simulated devices (distinct `deviceId`s), call revoke-all, assert both devices' refresh now fail.
- **Per-device revoke:** log in on 2 devices, `DELETE /auth/sessions/{deviceB}`, assert only device B fails.
- **`GET /auth/sessions`:** returns only the caller's own devices, never another user's, never a hash field.
- **TTL sliding + absolute cap:** requires introducing `TimeProvider` (`builder.Services.AddSingleton(TimeProvider.System)`, injected into the session issue/refresh path) + `Microsoft.Extensions.TimeProvider.Testing`'s `FakeTimeProvider` for tests — this codebase has **zero** existing `TimeProvider` usage (confirmed via grep), so this is a genuinely new pattern to introduce, worth flagging explicitly for `sdd-design`/`sdd-tasks` since nothing else in the codebase to copy from. Grace-window tests (~30s) could get away with real short waits; the 60d/180d TTL tests cannot.
- **`prv` still kills all:** reset a user's password, assert all previously-issued refresh tokens across multiple simulated devices are immediately rejected — this is also the *first* integration test coverage of `ResetPasswordAsync` at all (none exists today).
- **Cross-use protection changes shape, not just adds:** the existing `Refresh_WithAccessTokenInsteadOfRefreshToken_Returns401` (`LoginFlowTests.cs:86-96`) tested the `token_kind` claim check — under the opaque-token design this specific mechanism is gone; the equivalent new test is "an access token (or any arbitrary string) presented to `/auth/refresh` matches no `UserSession.CurrentTokenHash` row → 401," which is actually a *simpler* invariant (no claim-kind logic at all).

## Recommendation

Proceed to `sdd-propose`. Carry forward as concrete inputs:
1. Opaque, SHA-256-hashed refresh tokens on a new `UserSession : BaseEntity` (Current+Previous hash pair, no lineage table) — resolve the plan's ambiguous "`sid`/`jti`" phrasing in favor of this.
2. Auth0-style grace window (~30s, matching existing `ClockSkew` precedent) with the documented residual race risk stated explicitly, not hidden.
3. Explicit bulk-revoke of `UserSession` rows inside `ResetPasswordAsync`, alongside the unchanged `prv`/JWT path for `TokenKind.PasswordReset` itself.
4. No legacy-JWT dual-accept path — one-time forced re-login accepted as the deploy cost.
5. Decide explicitly in the proposal: delete `JwtOptions.RefreshSecret` (4 touch points) and introduce `TimeProvider` (new pattern, needed for TTL tests) — both flagged here, not silently resolved.
6. New `Options/UserSessionOptions.cs` for the three new TTL/grace-window settings, keeping `JwtOptions` JWT-signing-only.
7. Entity/endpoint naming: `UserSession`, `/auth/sessions*` — confirmed no collision with existing `AttentionSession`/`Features/Patients/Sessions`.
