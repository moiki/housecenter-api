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

- [ ] 2.1 `Features/Auth/AuthDtos.cs` — add `DeviceId` (required), `DeviceName?`, `Platform` to
      `LoginRequest` / `SignupRequest` / `RefreshRequest` (R10)
- [ ] 2.2 `Features/Auth/AuthValidators.cs` — `NotEmpty()` on `DeviceId`, `MaximumLength(200)`
      on `DeviceName`, `IsInEnum()` on `Platform` across the 3 validators (R10)
- [ ] 2.3 `Features/Auth/UserSessionService.cs` (new) — `IUserSessionService` /
      `UserSessionService`: `CreateOrRotateOnLoginAsync` — revoke existing active
      `(UserId, DeviceId)` row if present, insert fresh row, `Current=hash(T)`,
      `AbsoluteExpiresAt=now+180d`, `ExpiresAt=clamp` (R2, R6)
- [ ] 2.4 Same file — `RotateAsync`: tracked lookup `Current==hash || Previous==hash` incl.
      `User.Roles`; guard order — null session → 401; deviceId mismatch → revoke+401;
      `now>=ExpiresAt` → revoke(`ExpiredAbsolute`)+401; stale Previous
      (`now>PreviousTokenExpiresAt`) → revoke(`ReuseDetected`)+401; else rotate
      (`Previous=Current`, `PreviousExpiresAt=now+grace`, `Current=hash(T')`,
      `LastUsedAt=now`, `ExpiresAt=clamp(now)`) (R1, R3, R4, R5, R6)
- [ ] 2.5 `Features/Auth/AuthService.cs` — `LoginAsync`/`SignupAsync` call
      `CreateOrRotateOnLoginAsync`; `RefreshAsync` delegates entirely to `RotateAsync`, still
      mints the access JWT via unchanged `ITokenService` (R2, R3)
- [ ] 2.6 `Features/Auth/TokenService.cs` — remove `CreateRefreshToken`, the `Refresh` case in
      `SigningKeyFor`, the `TokenKind.Refresh` enum member (R1)
- [ ] 2.7 `Options/JwtOptions.cs` — delete `RefreshSecret` property
- [ ] 2.8 `Program.cs` — delete the `Jwt:RefreshSecret` `.Validate(...)` lines
- [ ] 2.9 `appsettings.json` — delete `Jwt:RefreshSecret`; `HouseCenter.Api.Tests/ApiFactory.cs`
      — delete the `["Jwt:RefreshSecret"]` config override
- [ ] 2.10 `CLAUDE.md` — update the "4 secrets" auth note to 3 (Access / Invitation / Reset)

**PR2 done when:** `dotnet build` clean, login/signup persist a `UserSession` row per device,
refresh rotates + revokes per the guard-order algorithm, zero remaining `RefreshSecret`
references repo-wide.

## Phase 3: Session Management + Reset — PR3 (~185 lines)

- [ ] 3.1 `Features/Auth/UserSessionService.cs` — add `RevokeDeviceAsync(userId, deviceId)`,
      `ListAsync(userId)` (`AsNoTracking()` + inline `new SessionResponse(...)` projection),
      `RevokeByIdAsync(userId, sessionId)` (`NotFound` for foreign/missing id, never
      `Forbidden`), `RevokeAllAsync(userId)`, `RevokeAllForPasswordResetAsync(userId)`
      (R7, R8, R9)
- [ ] 3.2 `Features/Auth/AuthDtos.cs` — `LogoutRequest(DeviceId)`, `SessionResponse(Id,
      DeviceId, DeviceName, Platform, LastUsedAt, CreatedDate)` — no hash fields (R9)
- [ ] 3.3 `Features/Auth/AuthValidators.cs` — `LogoutRequestValidator`
- [ ] 3.4 `Features/Auth/AuthEndpoints.cs` — `POST /auth/logout`, `GET /auth/sessions`,
      `DELETE /auth/sessions/{id:guid}`, `POST /auth/sessions/revoke-all`; all bare
      `.RequireAuthorization()`, `userId` from `ClaimTypes.NameIdentifier`, strictly
      self-scoped (R7)
- [ ] 3.5 `Features/Auth/AuthService.cs` — `ResetPasswordAsync` calls
      `RevokeAllForPasswordResetAsync` immediately after the existing `prv` bump (R8)

**PR3 done when:** `dotnet build` clean, all 4 endpoints self-scoped (foreign/nonexistent id →
404, never 403), `GET /auth/sessions` response contains zero hash fields.

## Phase 4: Tests — PR4 (~430 lines)

- [ ] 4.1 `HouseCenter.Api.Tests/HouseCenter.Api.Tests.csproj` — add
      `Microsoft.Extensions.TimeProvider.Testing`
- [ ] 4.2 `HouseCenter.Api.Tests/ApiFactory.cs` — `CreateAuthenticatedClientAsync` and every
      `LoginRequest`/`SignupRequest` call site gain `deviceId`/`platform`; add `public
      FakeTimeProvider Time` swap (`services.RemoveAll<TimeProvider>();
      services.AddSingleton<TimeProvider>(Time);`) — comment documenting the boundary: drives
      session TTL only, never JwtBearer's internal clock
- [ ] 4.3 New dedicated `ApiFactory`-backed fixture (own instance, **not** the shared
      `[Collection("Api")]`) for TTL tests — avoids `FakeTimeProvider` state bleeding across
      the shared collection
- [ ] 4.4 Extend `LoginFlowTests.cs` — `Refresh_RotatesToken_PreviousAndCurrentHashesUpdate`
      (R1, R3); `Refresh_ReuseAfterGrace_RevokesSessionAnd401sBoth` (R5);
      `Refresh_WithinGraceWindow_Accepted` (R4); `Refresh_OneSecondPastGrace_Returns401` (R4);
      `Refresh_DeviceIdMismatch_RevokesSession401` (R5);
      `Refresh_GarbageOrAccessToken_Returns401` (R1, R5)
- [ ] 4.5 New `SessionManagementTests.cs` — `Logout_RevokesOnlyThatDevice` (R7);
      `RevokeAll_RevokesEveryDevice` (R7); `DeleteSession_RevokesOnlyThatDevice_404ForForeignId`
      (R7); `GetSessions_SelfScopedNoHashFields` (R7, R9); `ResetPassword_RevokesAllSessions` (R8)
- [ ] 4.6 New `SessionTtlTests.cs` (dedicated fixture from 4.3) —
      `Refresh_SlidingTtlExtendsOnUse` (R6); `Refresh_AbsoluteCapNotExtendable_401AfterCap` (R6)
      — both drive the fake clock and then call `POST /auth/refresh` (session path), never a
      bearer-protected endpoint (JwtBearer clock is intentionally not time-driven)

**PR4 done when:** all 13 new scenario tests + the updated `LoginFlowTests.cs` pass green; the
dedicated TTL fixture is isolated from the shared `Api` collection; `dotnet test` full suite green.

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
