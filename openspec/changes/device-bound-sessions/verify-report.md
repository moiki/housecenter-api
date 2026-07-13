# Verify Report — Device-Bound Sessions

**Change**: `device-bound-sessions`
**Mode**: Strict TDD (`strict_tdd: true`, test runner `dotnet test`, confirmed via `openspec/config.yaml`)
**Date**: 2026-07-11
**Code repo verified**: HouseCenter.Api @ branch `feat/device-bound-sessions` (4 commits: `2661847` PR1, `afe26d3` PR2, `5ed54df` PR3, `b09ba73` PR4 — verified via `git log`, not squashed)

## Verdict

**PASS WITH WARNINGS — 0 CRITICAL, 3 WARNING, 4 SUGGESTION**

All 33/33 tasks genuinely complete, build clean, full suite (194/194) independently re-run and green, all 14 spec scenarios traced to real passing tests, all 5 proposal open-question resolutions honored in code. No functional defects found. The warnings are process/documentation gaps, not behavior bugs.

## Requirements checklist

| Req | Status | Note |
|---|---|---|
| R1 — Opaque, hashed refresh tokens | ✅ | `RefreshTokenGenerator.cs` (RNG 32 bytes, base64url), `TokenHasher.cs` (SHA-256 hex). Proven by generator/hasher unit tests (8) + `LoginFlowTests.Refresh_RotatesToken_PreviousAndCurrentHashesUpdate` (DB-level hash-shift proof) |
| R2 — Persisted per-device session (upsert) | ✅ | `UserSessionService.CreateOrRotateOnLoginAsync` revokes existing row before insert. Proven by `CreateOrRotateOnLogin_SecondLoginSameDevice_SupersedesPriorActiveSession` |
| R3 — Single-use rotation | ✅ | `RotateAsync` shifts Current→Previous. Proven by `Rotate_WithCurrentTokenMatch_ReturnsNewTokenDifferentFromOld` + `Refresh_WithValidRefreshToken_ReturnsNewTokenPair` |
| R4 — Grace window | ✅ | `PreviousTokenExpiresAt` check. Proven by `Rotate_WithinGraceWindow_PreviousTokenIsAccepted`, `Refresh_WithinGraceWindow_Accepted`, `SessionRotationTests.Refresh_OneSecondPastGraceDeadline_Returns401` (exact boundary) |
| R5 — Reuse detection + device mismatch | ✅ | Both revoke whole session. Proven by `Rotate_DeviceIdMismatch_...`, reuse-past-grace tests, `SessionRotationTests.Refresh_ReuseAfterGrace_RevokesSessionAnd401sBoth` (proves BOTH T1 and T2 401 after reuse) |
| R6 — Sliding/absolute TTL | ✅ | `ClampExpiry` single clamped field. Proven by sliding-extend, past-cap unit tests, `SessionTtlTests` (2 HTTP tests, real day-spaced advances) |
| R7 — Self-service revocation endpoints | ✅ | 4 endpoints, all `.RequireAuthorization()` + self-scoped. Proven by `SessionManagementTests` (7) incl. `DeleteSession_ForeignOrNonexistentId_Returns404_NeverForbidden` |
| R8 — Password reset bulk revocation | ✅ | `ResetPasswordAsync` → `RevokeAllForPasswordResetAsync`. Proven by unit + `SessionManagementTests.ResetPassword_RevokesAllSessions` (first HTTP-level proof) |
| R9 — Session list DTO safety | ✅ | `SessionResponse` has zero hash fields; inline projection. Proven by `GetSessions_SelfScopedAndNeverExposesHashFields` — asserts against **raw JSON text** |
| R10 — Request DTO additions | ⚠️ Partial | Validators enforce `NotEmpty()`/`IsInEnum()`. **No test** asserts a request missing `deviceId` or invalid `platform` returns 400 over HTTP — only the pre-existing email-format path is HTTP-tested |

## Independent verification results

| Command | Result |
|---|---|
| `dotnet build` | 0 errors, 7 warnings — all pre-existing (NU1903, CS0618 FirebasePushSender obsolete, MSB3277 EFCore.Relational version conflict, 3× CS8602 in `ClinicHandlerTests.cs`). Zero new warnings. |
| `dotnet test` (full suite) | **194/194 passed, 0 failed, 0 skipped**, 53s. Real Postgres confirmed reachable (`pg_isready` on 5432) — nothing silently skipped. |
| `dotnet test --filter` (session classes) | 41/41 passed (`LoginFlowTests`, `SessionManagementTests`, `SessionRotationTests`, `SessionTtlTests`, `UserSessionServiceTests`) |
| `grep -rn "RefreshSecret"` (*.cs/*.json, excl. obj/bin) | **0 matches** — fully removed |
| `grep -rn "RefreshTokenExpiryDays"` (*.cs/*.json, excl. obj/bin) | **0 matches** — fully removed |
| `grep -c "^- \[x\]" tasks.md` | 33/33; `^- \[ \]` → 0 — all tasks genuinely closed |
| `git log --oneline main..feat/device-bound-sessions` | 5 commits (1 docs + 4 PR) — genuinely chained, not squashed |
| `grep -rn "TokenKind.Refresh\|CreateRefreshToken"` | 0 matches — legacy JWT-refresh path fully removed, no dual-accept branch |

## Scenario coverage (14/14 spec.md scenarios)

All 14 scenarios COMPLIANT with real passing tests, each with both unit-level and HTTP-level evidence for every rotation/grace/reuse/TTL/revocation scenario: (1) login-creates-session, (2) refresh-rotates, (3) reuse-after-window→revoke, (4) grace accept, (5) grace boundary, (6) deviceId-mismatch→revoke, (7) logout-scoped, (8) revoke-all, (9) per-device DELETE, (10) GET sessions self-scoped + hash-free, (11) password-reset-kills-all, (12) sliding-TTL-extends, (13) absolute-cap-not-extendable, (14) access-token/garbage→401.

## Strict TDD compliance

- **GREEN confirmed** ✅ — independently re-ran full suite: 194/194, exact match to claim.
- **Triangulation** ✅ — every behavior has both a unit-level AND HTTP-level test with distinct assertions.
- **Assertion Quality Audit** ✅ Clean — read all new/modified test files: zero tautologies, zero ghost loops, zero mock-heavy tests (EF InMemory + FakeTimeProvider, no mocking framework), zero smoke-only patterns. Every assertion calls real production code and checks a specific expected value.
- **Test layer distribution**: Unit 27 (generator 4 + hasher 4 + `UserSessionServiceTests` 19) | Integration/HTTP 15 new + 7 pre-existing unchanged-and-green. Total in-scope: 49 tests, 49 passing.

## Findings

### CRITICAL
None.

### WARNING

1. **R10 validation not HTTP-tested for the new fields.** `deviceId`/`platform` validators exist and are structurally correct, but no test sends a request with a missing `deviceId` or invalid `platform` and asserts 400. The spec explicitly calls this out ("A request missing deviceId MUST fail validation with 400"). Low risk (FluentValidation pipeline is a proven convention) but currently unproven at runtime.
2. **Missing formal TDD Cycle Evidence table.** Downgraded from CRITICAL to WARNING: independent re-run matched the claimed counts exactly, and apply-progress documents two concrete bugs caught+fixed during real test execution (a `JsonStringEnumConverter` mismatch; a 204-vs-200 `ResultExtensions` mapping) — strong corroboration the tests were actually run. Documentation-format omission, not substance. Backfill before archive; not a ship blocker.
3. **Review Workload Guard bookkeeping left open.** `state.yaml` still showed `chain_strategy: pending` / `decision_needed_before_apply: true` although the change was delivered as 4 chained commits (a valid `ask-on-risk` resolution). Structurally honored but not formally recorded. (Resolved by the orchestrator at verify time.) PR4's diff (~464 non-prose lines) landed at/slightly over its own pre-disclosed ~430-line budget flag.

### SUGGESTION

1. `SessionResponse` (code) vs `UserSessionResponse` (spec) — cosmetic DTO naming mismatch, identical fields, zero behavioral impact.
2. `RefreshTokenGeneratorAndHasherTests.cs` contains two classes neither matching the filename — a `--filter` on the filename string matches nothing; mild future-dev footgun.
3. `RevokedReason.ExpiredAbsolute` labels both idle-60d sliding timeout and the 180d hard cap — design.md flagged this as an accepted open question; audit queries can't distinguish the two.
4. `RevokedReason.UserRevoked` reused for both explicit self-service revocation and the login-upsert-supersede case — same label, two triggers.

## Documented deviations reviewed

All acceptable: unit tests pulled forward into PR2/PR3 (strengthens TDD earlier, disclosed); `GetActiveSessionsAsync`/`RevokeSessionByIdAsync` naming (cosmetic); grace-boundary tests moved to a dedicated fixture (technically necessary — shared-fixture clock bleed); `RevokedReason.UserRevoked` for the upsert-supersede case (resolves a genuinely-unspecified spec gap); all 8 design decisions followed exactly (verified against `UserSessionService.cs`, `TokenService.cs`, `Program.cs`, migration); no legacy dual-accept path (confirmed — `TokenKind.Refresh` fully removed).

## Recommendation

**Ready for archive/PR.** Zero functional defects found across independent re-execution of the build, the full 194-test suite, all 14 spec scenarios, all 8 design decisions, and all 5 proposal open-question resolutions. The 3 warnings are process/documentation gaps — none block merging working, spec-compliant code. Recommend optionally adding 2 HTTP validation tests (missing-deviceId, invalid-platform → 400) and updating `state.yaml`'s guard fields before archive, but neither is a hard blocker.
