# Tasks: Device-Bound Sessions

## Review Workload Forecast

| Field | Value |
|---|---|
| Estimated changed lines (hand-written; excludes generated migration Designer.cs) | ~1,300–1,350 across full in-scope work |
| 400-line budget risk | High |
| Chained PRs recommended | Yes |
| Suggested split | 4 PRs — PR1 Foundation → PR2 Login/Refresh → PR3 Session Mgmt → PR4 Tests |
| Delivery strategy | ask-on-risk |
| Chain strategy | pending (orchestrator asks before apply, per ask-on-risk) |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: pending
400-line budget risk: High

**Basis:** `AuthService.cs` (178 lines today) gets ~130 lines touched across
`LoginAsync`/`SignupAsync`/`RefreshAsync`; `TokenService.cs` (119 lines) loses ~20 lines
(`Refresh` kind removal). `UserSessionConfiguration.cs` sized against
`PushSubscriptionConfiguration.cs` (28 lines, 1 index) at ~50–55 lines (4 indexes +
filtered-unique + 2 `HasConversion<string>()` enums + cascade). The `AddUserSessions`
migration's hand-written `Up`/`Down` sized against `AddAttentionSessions.cs` (90 lines, one
table + 4 FKs + 4 indexes) at ~110–120 lines (12 columns + 1 FK + 4 indexes). Generated
`*.Designer.cs` (precedent: `AddAttentionSessions.Designer.cs` = 973 lines, full model
snapshot) is **excluded** — never hand-reviewed line-by-line in this repo. `LoginFlowTests.cs`
(105 lines / 6 tests ≈ 17.5 lines/test, shared client, no fixture setup) scales to ~25–40
lines/test for the 13 new session scenarios (hash assertions, multi-request rotation chains,
a dedicated `FakeTimeProvider` fixture for the 2 TTL tests).
**Swing factors:** PR4 (~430 lines) lands at/slightly over its own 400-line budget purely
from test volume + dedicated-fixture plumbing — the tightest of the four slices. Merging the
TTL fixture setup into `SessionManagementTests.cs` saves ~40–60 lines but doesn't clear the
budget alone; a further 4a (rotation/revocation, shared fixture) / 4b (TTL, dedicated fixture)
split is the fallback if the reviewer wants strict per-PR compliance instead of the proposal's
approved 4-way plan.

**Pre-apply gates:** (1) each PR links an approved GitHub issue before implementation starts
(issue-first convention); (2) `ask-on-risk` delivery strategy — orchestrator asks the user to
confirm chain strategy (stacked-to-main vs feature-branch-chain) before `sdd-apply` runs PR1.

**Strict TDD note:** this repo is `strict_tdd: true`. PR1–PR3 ship production code without new
dedicated tests attached to each commit (existing `LoginFlowTests.cs` must stay green
throughout); PR4 batches the new scenario coverage as one reviewable slice. This is a
deliberate, proposal-approved exception to "tests in the same commit as behavior" — **not**
optional or deferred work. Tests are a first-class deliverable of this change; `sdd-apply`
MUST NOT treat PR4 as skippable or lower-priority than PR1–PR3.

---

## Phase 1: Foundation — PR1 (~330 lines)

- [x] 1.1 `Domain/Entities/UserSession.cs` — `UserSession : BaseEntity` (UserId, DeviceId,
      DeviceName, Platform, CurrentTokenHash, PreviousTokenHash, PreviousTokenExpiresAt,
      LastUsedAt, ExpiresAt, AbsoluteExpiresAt, RevokedAt, RevokedReason) + `DevicePlatform
      {Android,iOS,Web}` + `RevokedReason{UserLogout,UserRevoked,ReuseDetected,PasswordReset,
      ExpiredAbsolute}` enums; comment noting `DevicePlatform` is deliberately parallel to,
      not shared with, `PushPlatform` (R2)
- [x] 1.2 `Domain/Entities/User.cs` — add `ICollection<UserSession> Sessions` navigation
- [x] 1.3 `Data/Configurations/UserSessionConfiguration.cs` — `.HasConversion<string>()` on
      Platform/RevokedReason, max lengths (DeviceId 64, hashes 64), filtered-unique index on
      `CurrentTokenHash` (`HasFilter("\"IsActive\" = true")`), non-unique indexes on
      `PreviousTokenHash` / `UserId` / `(UserId, DeviceId)`, cascade delete on the User FK
      (R1, R2)
- [x] 1.4 `Data/HouseCenterDbContext.cs` — add `DbSet<UserSession> UserSessions`
- [x] 1.5 `dotnet ef migrations add AddUserSessions` + `dotnet ef database update` — hand-review
      only the non-Designer `.cs` (generated Designer snapshot excluded from budget)
- [x] 1.6 `Options/UserSessionOptions.cs` — `RefreshSlidingExpiryDays=60`,
      `RefreshAbsoluteExpiryDays=180`, `RefreshGraceWindowSeconds=30` (R6)
- [x] 1.7 `Features/Auth/RefreshTokenGenerator.cs` — `IRefreshTokenGenerator` /
      `RefreshTokenGenerator`: `RandomNumberGenerator.GetBytes(32)` → base64url (R1)
- [x] 1.8 `Features/Auth/TokenHasher.cs` — `ITokenHasher` / `TokenHasher`: SHA-256 hex; code
      comment stating fast-hash-is-correct-for-high-entropy-tokens (do not switch to BCrypt) (R1)
- [x] 1.9 `Program.cs` — `builder.Services.AddSingleton(TimeProvider.System)`; bind
      `UserSessionOptions`; register generator/hasher in DI (R6)

**PR1 done when:** `dotnet build` clean, migration applies, zero behavior change to existing
auth flows (`LoginFlowTests.cs` stays green unmodified).

## Phase 2: Login/Refresh Rewrite — PR2 (~365 lines)

- [x] 2.1 `Features/Auth/AuthDtos.cs` — add `DeviceId` (required), `DeviceName?`, `Platform` to
      `LoginRequest` / `SignupRequest` / `RefreshRequest` (R10)
- [x] 2.2 `Features/Auth/AuthValidators.cs` — `NotEmpty()` on `DeviceId`, `MaximumLength(200)`
      on `DeviceName`, `IsInEnum()` on `Platform` across the 3 validators (R10)
- [x] 2.3 `Features/Auth/UserSessionService.cs` (new) — `IUserSessionService` /
      `UserSessionService`: `CreateOrRotateOnLoginAsync` — revoke existing active
      `(UserId, DeviceId)` row if present, insert fresh row, `Current=hash(T)`,
      `AbsoluteExpiresAt=now+180d`, `ExpiresAt=clamp` (R2, R6)
- [x] 2.4 Same file — `RotateAsync`: tracked lookup `Current==hash || Previous==hash` incl.
      `User.Roles`; guard order — null session → 401; deviceId mismatch → revoke+401;
      `now>=ExpiresAt` → revoke(`ExpiredAbsolute`)+401; stale Previous
      (`now>PreviousTokenExpiresAt`) → revoke(`ReuseDetected`)+401; else rotate
      (`Previous=Current`, `PreviousExpiresAt=now+grace`, `Current=hash(T')`,
      `LastUsedAt=now`, `ExpiresAt=clamp(now)`) (R1, R3, R4, R5, R6)
- [x] 2.5 `Features/Auth/AuthService.cs` — `LoginAsync`/`SignupAsync` call
      `CreateOrRotateOnLoginAsync`; `RefreshAsync` delegates entirely to `RotateAsync`, still
      mints the access JWT via unchanged `ITokenService` (R2, R3)
- [x] 2.6 `Features/Auth/TokenService.cs` — remove `CreateRefreshToken`, the `Refresh` case in
      `SigningKeyFor`, the `TokenKind.Refresh` enum member (R1)
- [x] 2.7 `Options/JwtOptions.cs` — delete `RefreshSecret` property
- [x] 2.8 `Program.cs` — delete the `Jwt:RefreshSecret` `.Validate(...)` lines
- [x] 2.9 `appsettings.json` — delete `Jwt:RefreshSecret`; `HouseCenter.Api.Tests/ApiFactory.cs`
      — delete the `["Jwt:RefreshSecret"]` config override
- [x] 2.10 `CLAUDE.md` — update the "4 secrets" auth note to 3 (Access / Invitation / Reset)
- [x] 2.11 (deviated from Phase 4 plan, per this run's explicit override) `HouseCenter.Api.Tests/UserSessionServiceTests.cs` (new) —
      10 unit tests for `UserSessionService` using EF InMemory + `FakeTimeProvider`: rotate on
      Current match, grace-window accept, grace-window boundary (+1s past deadline), reuse well
      after grace, no-match/garbage token (nothing revoked), deviceId mismatch (revoked),
      sliding TTL extends, sliding clamps to absolute cap, absolute cap enforced (401+revoke),
      login-upsert supersedes prior session (R1-R6, R2)
- [x] 2.12 `HouseCenter.Api.Tests/HouseCenter.Api.Tests.csproj` — add
      `Microsoft.Extensions.TimeProvider.Testing` (pulled forward from task 4.1 to unblock 2.11)

**PR2 done when:** `dotnet build` clean, login/signup persist a `UserSession` row per device,
refresh rotates + revokes per the guard-order algorithm, zero remaining `RefreshSecret`
references repo-wide.

## Phase 3: Session Management + Reset — PR3 (~185 lines)

- [x] 3.1 `Features/Auth/UserSessionService.cs` — add `RevokeDeviceAsync(userId, deviceId)`,
      `ListAsync(userId)` (`AsNoTracking()` + inline `new SessionResponse(...)` projection),
      `RevokeByIdAsync(userId, sessionId)` (`NotFound` for foreign/missing id, never
      `Forbidden`), `RevokeAllAsync(userId)`, `RevokeAllForPasswordResetAsync(userId)`
      (R7, R8, R9)
      — implemented as `GetActiveSessionsAsync`/`RevokeSessionByIdAsync` per this run's
      explicit apply-time instruction (functionally identical to `ListAsync`/`RevokeByIdAsync`;
      `RevokeSessionByIdAsync` still returns `Result` — NotFound on foreign/missing id — to
      stay consistent with every other Auth-endpoint's `.ToHttpResult()` convention). Also
      added unit tests (pulled forward from PR4 per this run's strict-TDD override, same
      precedent as PR2's task 2.11): list-self-scoped, revoke-device-self-scoped +
      idempotent-no-op, revoke-all-self-scoped, revoke-by-id-owned/foreign/nonexistent,
      password-reset-bulk-revoke-self-scoped + no-op-when-no-sessions (9 new tests in
      `UserSessionServiceTests.cs`, alongside PR2's 10 — HTTP-level equivalents remain in PR4).
- [x] 3.2 `Features/Auth/AuthDtos.cs` — `LogoutRequest(DeviceId)`, `SessionResponse(Id,
      DeviceId, DeviceName, Platform, LastUsedAt, CreatedDate)` — no hash fields (R9)
- [x] 3.3 `Features/Auth/AuthValidators.cs` — `LogoutRequestValidator`
- [x] 3.4 `Features/Auth/AuthEndpoints.cs` — `POST /auth/logout`, `GET /auth/sessions`,
      `DELETE /auth/sessions/{id:guid}`, `POST /auth/sessions/revoke-all`; all bare
      `.RequireAuthorization()`, `userId` from `ClaimTypes.NameIdentifier`, strictly
      self-scoped (R7)
- [x] 3.5 `Features/Auth/AuthService.cs` — `ResetPasswordAsync` calls
      `RevokeAllForPasswordResetAsync` immediately after the existing `prv` bump (R8); fixed
      the stale `PasswordRecoveryVersion++` comment to reflect that opaque sessions are no
      longer tied to `prv` (PR2 already made refresh opaque) — the explicit bulk-revoke is
      what enforces R8 now
- [x] 3.6 (trivial cleanup, in-scope per apply instructions) `Options/JwtOptions.cs` +
      `appsettings.json` — removed the now-dead `RefreshTokenExpiryDays` (unused since PR2
      made refresh opaque/DB-driven TTL via `UserSessionOptions`); confirmed zero remaining
      references repo-wide via grep before removing

**PR3 done when:** `dotnet build` clean, all 4 endpoints self-scoped (foreign/nonexistent id →
404, never 403), `GET /auth/sessions` response contains zero hash fields.

## Phase 4: Tests — PR4 (~430 lines)

- [x] 4.1 (done early in PR2, task 2.12 — package was needed to unblock `UserSessionServiceTests`
      unit tests) `HouseCenter.Api.Tests/HouseCenter.Api.Tests.csproj` — add
      `Microsoft.Extensions.TimeProvider.Testing`
- [x] 4.2 `HouseCenter.Api.Tests/ApiFactory.cs` — the `deviceId`/`platform` ripple across
      `CreateAuthenticatedClientAsync` and every `LoginRequest`/`SignupRequest` call site
      repo-wide was already done in PR2 (breaking DTO change, contained then to keep the full
      suite green). Added `public FakeTimeProvider Time` swap
      (`services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(Time);`) —
      comment documenting the boundary: drives session TTL only, never JwtBearer's internal
      clock. Wired unconditionally on every `ApiFactory` instance (shared + dedicated) —
      inert unless a test calls `.Advance()`, so it's harmless for the shared fixture.
- [x] 4.3 Dedicated `ApiFactory`-backed fixtures for time-driven tests — implemented as a
      fresh `new ApiFactory()` constructed **inside each `[Fact]`** (own `Time` instance,
      `await using`-disposed at test end) rather than a single shared `IClassFixture`, so
      each time-driven test gets a completely isolated clock. Both dedicated-fixture test
      classes (`SessionRotationTests`, `SessionTtlTests`) are still tagged
      `[Collection("Api")]` (without consuming the shared instance) purely so xunit runs them
      sequentially with the rest of the HTTP suite — a dedicated `ApiFactory` drops/recreates
      the SAME real `housecenter_test` Postgres schema on startup, which must never race with
      another instance's in-flight requests. Avoids `FakeTimeProvider` state bleeding across
      the shared collection (design.md "shared-fixture caution").
- [x] 4.4 Extended `LoginFlowTests.cs` (no time control needed, shared fixture) —
      `Refresh_RotatesToken_PreviousAndCurrentHashesUpdate` (R1, R3, DB-level hash-shift
      proof via `ITokenHasher`); `Refresh_WithinGraceWindow_Accepted` (R4, immediate retry,
      no time control needed); `Refresh_DeviceIdMismatch_RevokesSession401` (R5);
      `Refresh_WithGarbageToken_Returns401` (R1, R5, new — alongside the pre-existing
      `Refresh_WithAccessTokenInsteadOfRefreshToken_Returns401`, left unchanged/still
      correct). **Deviation:** `Refresh_ReuseAfterGrace_RevokesSessionAnd401sBoth` and
      `Refresh_OneSecondPastGrace_Returns401` moved to the new dedicated-fixture
      `SessionRotationTests.cs` instead of living in `LoginFlowTests.cs` — both need to
      advance time past the grace deadline, which the shared fixture cannot safely do
      (see 4.3's rationale); documented here rather than silently relocated.
- [x] 4.5 New `SessionManagementTests.cs` — `Logout_RevokesOnlyThatDevice` (R7);
      `RevokeAll_RevokesEveryDevice` (R7); `DeleteSession_RevokesOnlyThatDevice` +
      `DeleteSession_ForeignOrNonexistentId_Returns404_NeverForbidden` (R7, split into two
      tests instead of one for clarity); `GetSessions_SelfScopedAndNeverExposesHashFields`
      (R7, R9 — asserts against the raw JSON text, not just the deserialized DTO);
      `SessionEndpoints_WithoutAuth_Return401` (R7, all 4 endpoints); `ResetPassword_
      RevokesAllSessions` (R8, first end-to-end coverage of `ResetPasswordAsync`, uses a
      dedicated seeded Member user — never the shared Owner account, to avoid corrupting
      `ApiFactory.OwnerPassword` for every other test in the collection)
- [x] 4.6 New `SessionTtlTests.cs` (dedicated fixture per 4.3) —
      `Refresh_SlidingTtlExtendsOnUse` (R6); `Refresh_AbsoluteCapNotExtendable_401AfterCap` (R6)
      — both drive the fake clock and then call `POST /auth/refresh` (session path), never a
      bearer-protected endpoint (JwtBearer clock is intentionally not time-driven)

**PR4 done when:** all 13 new scenario tests + the updated `LoginFlowTests.cs` pass green; the
dedicated TTL fixture is isolated from the shared `Api` collection; `dotnet test` full suite green.

**PR4 actual delivery (2026-07-12):** 15 new HTTP-integration tests shipped (4 in
`LoginFlowTests.cs`, 2 in `SessionRotationTests.cs`, 7 in `SessionManagementTests.cs`, 2 in
`SessionTtlTests.cs` — one more than the originally-cataloged 13+updated-existing, because
`DeleteSession_...` was split into two focused tests and `SessionEndpoints_WithoutAuth_
Return401` covers all 4 unauthenticated-access assertions in one test per R7). `dotnet build`
clean (0 errors, same 7 pre-existing warnings as PR1-3, zero new). Full `dotnet test`:
194/194 passed, 0 skipped (Postgres reachable throughout) = 179 PR1-3 baseline + 15 new.
**ALL 4 PHASES OF `device-bound-sessions` ARE NOW COMPLETE.**

---

## Risks / gotchas to honor during apply

- **Fixture bleed:** `ApiFactory` is a `[Collection("Api")]` shared fixture — a mutated
  `FakeTimeProvider` on it leaks across every test in the collection. TTL tests MUST use the
  dedicated instance from 4.3, not the shared one.
- **TimeProvider boundary:** `FakeTimeProvider` drives session `ExpiresAt`/`AbsoluteExpiresAt`
  only. JwtBearer access-token expiry uses its own internal clock — advancing the fake clock
  60/180 days does nothing to bearer-protected endpoints; TTL assertions must go through
  `/auth/refresh`.
- **Filtered-unique-index casing:** `HasFilter("\"IsActive\" = true")` must exactly match
  Postgres' quoted column casing as EF generates it in the migration SQL — verify the generated
  migration's `CREATE UNIQUE INDEX ... WHERE` clause, not just the C# fluent call.
- **Breaking DTO change:** `deviceId` becomes required on `LoginRequest`/`SignupRequest`/
  `RefreshRequest` — every existing caller (test or client) must be updated; centralizing test
  logins through `ApiFactory.CreateAuthenticatedClientAsync` (4.2) contains most of the ripple.
- **Documented residual race** (design, accepted): two concurrent refreshes of the same
  Current token can both mint valid pairs inside the ~30s grace window before either is
  flagged — out of scope for this change, not a task-level defect.
- **PR4 budget:** ~430 lines is at/slightly over the 400-line guard on its own — flagged above
  for reviewer judgment or an optional 4a/4b split.
