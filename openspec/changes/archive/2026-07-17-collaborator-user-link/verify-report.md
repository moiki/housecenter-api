# Verification Report: Collaborator ↔ User Link

**Change**: `collaborator-user-link`
**Branch**: `feat/collaborator-user-link-reports` (4 stacked commits)
**Verdict**: **PASS WITH WARNINGS** — 0 CRITICAL, 3 WARNING, 2 SUGGESTION

## Build & Tests

**Build**: `dotnet build` succeeded, 0 errors, 5 pre-existing warnings (NU1903 OpenApi advisory, MSB3277 EF Relational version conflict, unrelated to this change).

**Tests**: `dotnet test` — **243/243 passing** (40.7s). Matches apply-progress's reported final count exactly.

## Spec Compliance Matrix

All scenarios have passing test coverage:

| Capability | Requirements | Scenario Count | Test Evidence | Status |
|---|---|---|---|---|
| `collaborator-user-link` | 5 (unique-link, unique-email, signup, POST endpoint, legacy-visibility) | 9 scenarios | `CollaboratorUserLinkTests.cs`, `CollaboratorCrudTests.cs`, `SignupFlowTests.cs`, `CollaboratorReconciliationServiceTests.cs`, `CollaboratorReconciliationTests.cs`, `CollaboratorEmailUniqueIndexTests.cs` | ✅ COMPLIANT |
| `sponsor-reports` (modified) | 3 (summary-count, clinic/route-detail, session-breakdown) | 3 scenarios | `ReportTests.cs` 2 new tests: `GetSessionPeriod_ByCollaborator_ResolvesRosterNameViaLink_ClinicReportStillCountsLegacy`, `Collaborators_RosterCount_IncludesLegacy_SessionActivityPopulation_ExcludesLegacy` | ✅ COMPLIANT |

## Key Findings

### WARNING 1 — Task-count metadata mismatch (non-blocking)

`openspec/changes/collaborator-user-link/tasks.md` actually contains **21** checked `[x]` tasks (Phase1=5, Phase2=6, Phase3=6, Phase4=4), not 19 as stated by both the tasks artifact and apply-progress ("19 tasks across 4 phases... 19/19 complete"). All 21 are checked, zero incomplete — so this is a documentation/count-reporting error in the artifacts, not a functional gap. No fix needed for archive, but worth correcting the count next time tasks.md is regenerated.

### WARNING 2 — GetSessionPeriodReport.cs deviation, evaluated and judged ACCEPTABLE, not a bug

`design.md`'s literal snippet groups by `{CollaboratorId, Name}`; the implementation (`HouseCenter.Api/Features/Reports/GetSessionPeriodReport.cs:78-93`) groups the final dictionary by resolved `Name` string only. 

**Analysis**: `SessionPeriodReportResponse.SessionsByCollaborator` (`ReportDtos.cs:41`) is `IReadOnlyDictionary<string,int>` — this type was NOT touched by PR4 (pre-existing from the original `sponsor-reports` change), so a name-keyed dictionary was already the committed contract regardless of which grouping key is used internally. Design's literal `{CollaboratorId, Name}` grouping would still collide at the final `ToDictionaryAsync(x => x.Name, ...)` step if two different collaborators resolved to the same display name — i.e. the crash risk is inherent to the pre-existing response type, not introduced by this deviation. Grouping directly by Name (as implemented) converts a potential runtime crash (`ArgumentException: An item with the same key has already been added`) into a silent count-merge for two people who happen to share a full name — strictly safer for a report endpoint. Judged as a reasonable, documented, defensive choice given the immutable type constraint; flagged WARNING only because a name collision would silently misattribute one person's session count to another's roster identity (rare in practice — Nicaraguan full-name collisions in a small NGO roster are unlikely but not impossible).

**SUGGESTION**: if this ever becomes a real risk, extend `SessionPeriodReportResponse` to key by `CollaboratorId` (or `Guid|"unlinked"` composite) instead of display name — out of scope for this change.

### WARNING 3 — Task 3.5 "migration test" is a proxy, not a literal migrate-over-dirty-data test

Tasks.md 3.5 says "migration test — unique index fails fast on pre-existing dupes." What exists is `CollaboratorEmailUniqueIndexTests.cs` (Postgres integration, real DB, real `DbUpdateException` on duplicate insert) — this proves the **already-applied** unique index rejects duplicates via `SaveChangesAsync`, which is equivalent behavioral evidence (Postgres `CREATE UNIQUE INDEX` uses the same b-tree constraint mechanism and would fail identically against pre-existing dupes), but there is no test that literally seeds duplicate emails BEFORE applying `AddCollaboratorEmailUnique` and asserts the migration's `Up()` throws.

**Note**: Downgraded to SUGGESTION-adjacent since the underlying DB mechanism is well-established Postgres behavior — not blocking, but a stricter literal test would strengthen migration-specific TDD evidence next time.

## Specific Verification Points

1. ✅ Every spec scenario has real test coverage exercising GIVEN/WHEN/THEN, not just name-matching — verified by reading test bodies, not just names.

2. ✅ GetSessionPeriodReport deviation — analyzed above, acceptable given pre-existing Dictionary<string,int> contract.

3. ✅ `AuthService.SignupAsync` — confirmed: Member signup NEVER calls `new Collaborator`/`db.Collaborators.Add` — only `existingCollaborator.UserId = user.Id` (link) or a `LogWarning` (no link). Matches design.

4. ✅ Unique constraints confirmed present:
   - Partial-unique `UserId` index: `EmployeeConfiguration.cs:43-45`, `Migrations/20260717155355_AddCollaboratorUserLink.cs:20-25`
   - Plain unique `Email` index: `EmployeeConfiguration.cs:54-55`, `Migrations/20260717175717_AddCollaboratorEmailUnique.cs:13-17`
   - Real-Postgres rejection test: `CollaboratorEmailUniqueIndexTests.cs` uses `ApiFactory` (WebApplicationFactory against real `housecenter_test` Postgres DB) and asserts `DbUpdateException` on duplicate-email insert

5. ✅ Migration B safety: pure additive `CreateIndex(unique: true)` with no try/catch, no data mutation — will fail loudly (standard Postgres behavior) if pre-existing duplicates exist. No silencing.

6. ✅ Via git history: zero Translation-feature files touched across all 4 commits (5322ad7, 52edaa3, 442c65a, 522843d). `git status` before verify shows Translation files modified-unstaged and untracked, byte-identical to apply-progress's claim. `Program.cs`'s only change is exactly the expected 1-line DI registration in PR3.

7. ✅ Confirmed via git diff: NONE of AttentionSession/MedicalConsultation/Notification/PushSubscription/Sponsor entities touched across the 4-PR chain. Out-of-scope domains untouched.

## Minor unaudited item (not flagged as issue)

`design.md`'s File Changes table listed `CollaboratorValidators.cs` for an "Optional UserId rule" modification, but git history shows this file was never touched by any of the 4 commits (last touched in original migration commit `0f4f7ef`). No task in tasks.md explicitly assigned this work either. Since `UserId` is an optional `Guid?` requiring no format-level validation (all domain checks — not-found/not-Member/already-linked — are handled in `CollaboratorService.ResolveUserIdAsync`, which is the correct layer), this is harmless design/tasks drift, not a functional gap. Not flagged as WARNING/CRITICAL.

## Verification Summary

- Build: ✅ Passes, 0 errors
- Tests: ✅ 243/243 passing, all spec scenarios covered
- Spec compliance: ✅ All requirements have test evidence
- Code review: ✅ Deviations evaluated and acceptable
- Migrations: ✅ Deterministic, safe, no blind mutations
- Constraints: ✅ DB-enforced (partial-unique UserId, unique Email)
- Domain boundaries: ✅ No unintended out-of-scope entity touches
- Test coverage: ✅ Unit + integration + real Postgres DB migrations

**Recommendation**: APPROVED FOR ARCHIVE. Warnings are documentation/design-spec-drift issues, not functional blockers. All 4 PRs successfully stacked and tested.

---

**Verified**: 2026-07-17 18:33:55
**Verifier**: SDD Verify Phase
**Branch**: `feat/collaborator-user-link-reports`
**Commit Range**: 5322ad7..522843d (4 commits, all PRs for this change)
