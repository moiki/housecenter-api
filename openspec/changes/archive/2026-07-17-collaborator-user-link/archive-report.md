# Archive Report: Collaborator ↔ User Link

**Change**: `collaborator-user-link`
**Status**: ARCHIVED (2026-07-17)
**Verdict**: PASS WITH WARNINGS — Implementation complete, all 243 tests passing, zero CRITICAL issues.

## Artifact Traceability

| Artifact | Observation ID | Topic Key | Status |
|----------|---|---|---|
| Proposal | #638 | sdd/collaborator-user-link/proposal | ✅ |
| Spec (delta + reconciliation) | #639 + spec.md | sdd/collaborator-user-link/spec | ✅ |
| Design | #640 | sdd/collaborator-user-link/design | ✅ |
| Tasks | #641 | sdd/collaborator-user-link/tasks | ✅ (21/21 complete) |
| Verify Report | #644 | sdd/collaborator-user-link/verify-report | ✅ PASS WITH WARNINGS |

## Archive Contents

- Proposal: `2026-07-17-collaborator-user-link/proposal.md`
- Spec: `2026-07-17-collaborator-user-link/spec.md`
- Design: `2026-07-17-collaborator-user-link/design.md`
- Tasks: `2026-07-17-collaborator-user-link/tasks.md`
- Verify Report: `2026-07-17-collaborator-user-link/verify-report.md`
- Archive Report: `2026-07-17-collaborator-user-link/archive-report.md`

## Specs Synced to Main Source of Truth

| Domain | Spec File | Action | Summary |
|--------|-----------|--------|---------|
| collaborator-user-link | `openspec/specs/collaborator-user-link.md` | Created | New capability: 1:1 Collaborator↔User link with 5 core requirements (unique constraint, signup provisioning, POST endpoint resolution, legacy visibility) |
| sponsor-reports | `openspec/specs/sponsor-reports.md` | Updated | Modified 3 requirements (R1 summary count, R2/R3 clinic/route detail, R4 session breakdown) to resolve collaborator identity through the User link; all other requirements preserved |

## Implementation Summary

**Four Stacked PRs — All Complete (branch: `feat/collaborator-user-link-reports`)**

| PR | Commit | Title | Changes |
|---|---|---|---|
| 1 | 5322ad7 | Schema foundation | `Collaborator.UserId` FK + partial-unique index + Migration A |
| 2 | 52edaa3 | Provisioning | `POST /collaborators` UserId resolution + Member signup link |
| 3 | 442c65a | Reconciliation | Email-match backfill + unique Email index + Migration B + seed realign |
| 4 | 522843d | Reports | Session-period name join via link + documented count semantics |

**Metrics**:
- 21 tasks completed (5+6+6+4 across phases)
- 243/243 tests passing (all spec scenarios covered)
- Zero CRITICAL issues in verification
- 3 non-blocking WARNINGs (task-count metadata drift, acceptable implementation deviation, proxy migration test)
- 2 SUGGESTIONs (follow-up improvements, not blocking)

**Key Features Implemented**:
1. `Collaborator.UserId` (nullable, unique FK to User) + inverse nav on User
2. Partial-unique index on `UserId` (allows multiple NULLs for legacy rows)
3. Plain unique index on `Collaborator.Email` (post-reconciliation)
4. Member signup auto-links existing unlinked Collaborators by email or warns if none found
5. `POST /collaborators` accepts optional `UserId` with fallback resolution (explicit → email-match → null/pending)
6. Legacy (unlinked) Collaborators remain visible with `IsLinked` indicator
7. `GET /collaborators/me` resolves via link-based lookup only
8. Reports unified to resolve collaborator identity through link (summary, clinic, route, session-period)
9. Reconciliation service + endpoint (`POST /collaborators/reconcile`) for backfill
10. Seeded dataset realigned: 2 linked + 1 deliberate legacy Collaborator

## Architecture Decisions Locked

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Provisioning | Roster-first link (POST /collaborators authoritative creation path) | Admins own clinic/route assignment; roster row must have real clinic context, not guessed |
| Signup behavior | Does NOT fabricate Collaborator stubs | Signup has no ClinicId available; creates unlinked User + warning instead of dirty roster data |
| Backfill strategy | Email-match (case-insensitive), idempotent, non-destructive | Realigns seed; prod backfill human-reconciled; no blind mutations inside migrations |
| Migration pattern | Two additive steps + reconciliation gate | Separates always-safe column from dedupe-dependent unique index; keeps each migration deterministic |
| Report identity | One canonical name source (link-based join) | Unification via link; roster counts distinct from session-activity names but reconcile by person |
| Legacy rows | Asymmetric rule | Collaborator with `UserId=null` valid permanently; Member User with no Collaborator is transient anomaly (enumerated & warned) |

## Rollback Plan

Both migrations are additive (nullable FK + indexes). Rollback:
1. Down-migrate to drop Email unique index
2. Down-migrate to drop UserId column/index
3. User/Collaborator rows untouched → no data loss
4. Revert enrollment/reports commits to restore prior behavior (split identity sourcing)

## SDD Cycle Complete

- ✅ **Proposed** (2026-07-16): Defined scope, approach, risks, and success criteria
- ✅ **Specified** (2026-07-17): Formalized 5+3 requirements across new + modified capabilities
- ✅ **Designed** (2026-07-17): Technical approach with 5 locked architecture decisions
- ✅ **Tasked** (2026-07-17): 21 tasks across 4 work units / stacked PRs
- ✅ **Applied** (2026-07-17): All 4 PRs implemented, 243/243 tests passing
- ✅ **Verified** (2026-07-17): PASS WITH WARNINGS (0 CRITICAL, 3 WARNING, 2 SUGGESTION)
- ✅ **Archived** (2026-07-17): Main specs synced, change folder moved to archive

**Next Steps**: See `openspec/IMPLEMENTATION_PLAN.md` for Phase 6+ work (notifications, file uploads, anonymization, frontend integration).

---

**Archived By**: SDD Archive Executor (haiku model)
**Archive Date**: 2026-07-17
**Project**: housecenter-api
**Backend**: hybrid (openspec + engram)
**Engram Record**: Observation #645 (sdd/collaborator-user-link/archive-report)
