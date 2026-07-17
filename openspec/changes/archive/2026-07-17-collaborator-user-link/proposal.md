# Proposal: Collaborator ↔ User Link (1:1)

## Change name
`collaborator-user-link`

## Intent

`User` (login/JWT) and `Collaborator` (roster: Clinic/WorkRoute/Positions) are two
decoupled person identities with no FK between them, created by two flows that never cross.
Consequences today:

- Domain V2 (AttentionSessions, MedicalConsultations, Notifications, PushSubscriptions)
  keys off `User`; Reports + Clinic/WorkRoute roster key off `Collaborator`. The **same
  Reports module mixes both sources** (`GetSummaryReport`/`GetClinicReport`/`GetWorkRouteReport`
  count from `db.Collaborators`; `GetSessionPeriodReport` groups by `AttentionSession.Collaborator`,
  a `User`) → counts can diverge.
- A `Collaborator` with no `User` can never log in, record sessions, open consultations, or
  get notified. A Member `User` with no `Collaborator` sits outside roster/reports.
- `Collaborator.Email` has no unique constraint (`User.Email` does).

**Goal**: make field collaborators first-class in Doctor communication and make roster/reports
reflect who actually works — enforced by a real DB constraint. Architecture decision **B is
already chosen** (do not reopen A/C).

## Scope

### In Scope
- Add `Collaborator.UserId` FK → `User` (1:1, unique) + nav; nullable at DB level for phased backfill.
- EF config (unique index on `UserId`, add unique index on `Collaborator.Email`) + migration.
- Member enrollment flow: creating a Role=Member `User` requires/auto-provisions a linked `Collaborator`; `POST /collaborators` binds to a `User`.
- Unify Reports to resolve collaborator identity through the link (single source of truth).
- Backfill/reconciliation plan for existing rows (seed + real data).

### Out of Scope
- No changes to `AttentionSession`/`MedicalConsultation`/`Notification`/`PushSubscription` FKs — already correctly bound to `User`.
- No Sponsor model change; no collaborator↔sponsor channel (intentional privacy).
- Merging `Collaborator` into `User` (Option A, rejected).
- Residual `Employee→Collaborator` rename (`Position.EmployeeId`, `EmployeeConfiguration.cs`) — deferred to its own slice.
- Frontend integration.

## Capabilities

### New Capabilities
- `collaborator-user-link`: 1:1 `Collaborator`↔`User` relationship, Member enrollment provisioning rules, unique-email constraint, backfill/legacy reconciliation.

### Modified Capabilities
- `sponsor-reports`: collaborator counts/names resolved through the `User` link so summary and session-period reports reconcile.

## Approach (Option B)

`Collaborator` becomes the **field profile** of a Member `User`, not a parallel identity.

- Nullable `UserId` at DB level → allows migrating existing rows without a hard cutover; presence is enforced at the app layer for new Member enrollment, so legacy rows may remain unlinked ("legacy") pending manual reconciliation.
- Member signup auto-provisions or links a `Collaborator`; `POST /collaborators` sets the `User` link.
- Reports read collaborator identity via the link; direct `db.Collaborators` name/count paths that diverge from session data are retired.
- Backfill: match `Collaborator.Email` ↔ `User.Email` (case-insensitive) to populate `UserId`; unmatched Collaborators stay flagged legacy; Member Users without a Collaborator get a provisioned roster stub.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `Domain/Entities/Collaborator.cs` | Modified | Add `UserId` + `User` nav |
| `Data/Configurations/CollaboratorConfiguration.cs` | Modified | Unique index on `UserId` and `Email` |
| EF migration | New | Additive: nullable FK column + indexes |
| `Features/Collaborators/CollaboratorService.cs` | Modified | Link on create, enforce unique email |
| `Features/Auth/AuthService.cs` | Modified | Member signup provisions/links `Collaborator` |
| `Features/Seeding/TestDataSeeder.cs` | Modified | Align seeded Collaborator emails with Member Users + set link |
| `Features/Reports/*` | Modified | Single collaborator source via the link |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Existing unlinked Collaborators / Members without a Collaborator | Med | Email-match backfill; leave unmatched as flagged legacy with a follow-up plan |
| Duplicate `Collaborator.Email` blocks the unique index | Med | Detect + dedupe before applying the migration |
| 1:1 too rigid vs reality (one person, multiple roster stints) | Low | 1:1 acceptable now; revisit if history is needed |
| Report numbers shift after unification | Med | Document expected deltas; snapshot before/after |

## Rollback Plan

Migration is additive (nullable FK + indexes) — down-migration drops the `UserId` column and
indexes; original `User`/`Collaborator` rows are untouched, so no data loss. Revert the
enrollment/reports commits to restore prior behavior.

## Dependencies

- None external. Builds on Domain V2 (already implemented).

## Success Criteria

- [ ] Every new Role=Member `User` has a linked `Collaborator` (and vice versa), enforced at enrollment.
- [ ] DB unique constraint on `Collaborator.UserId` and `Collaborator.Email`.
- [ ] Reports derive collaborator identity from a single source; summary vs session-period counts reconcile.
- [ ] Legacy unlinked rows are enumerated with a documented backfill outcome.
