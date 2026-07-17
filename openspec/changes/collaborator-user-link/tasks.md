# Tasks: Collaborator ↔ User Link

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~900-1100 |
| 400-line budget risk | High |
| Chained PRs recommended | Yes |
| Suggested split | PR1 Schema → PR2 Provisioning → PR3 Reconciliation → PR4 Reports |
| Delivery strategy | ask-on-risk |
| Chain strategy | pending |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: pending
400-line budget risk: High

### Suggested Work Units

| Unit | Goal | PR | Notes |
|------|------|----|----|
| 1 | Schema: `UserId` FK + indexes + Migration A | 1 | Additive only, ~250 lines |
| 2 | `POST /collaborators` UserId resolution + signup link | 2 | Depends on PR1, ~350 lines |
| 3 | Reconciliation service/endpoint + Migration B + seed realign | 3 | Depends on PR1 |
| 4 | Reports: session name join + count-semantics docs | 4 | Depends on PR1 |

## Decisions Resolved (were open in design)

1. **Gap exposure**: reconciliation endpoint response returns the unlinked-Member-User list + structured warn logs (signup, reconcile). No new GET report endpoint this phase — keeps PR 3 scope tight.
2. **Reconciliation trigger**: `POST /collaborators/reconcile`, `AdministratorOrAbove`, idempotent, runs in **all environments** — unlike `/seed`'s Dev/Staging gate (fake data only), prod needs real backfill.

## Phase 1: Schema Foundation (PR 1)

- [x] 1.1 `Domain/Entities/Collaborator.cs`: add `Guid? UserId`, `User? User` nav.
- [x] 1.2 `Domain/Entities/User.cs`: add inverse `Collaborator?` nav.
- [x] 1.3 `EmployeeConfiguration.cs`: `HasOne(User).WithOne` FK, `OnDelete(SetNull)`, partial-unique `UserId` index (Email unique index deferred to Phase 3/Migration B).
- [x] 1.4 `dotnet ef migrations add AddCollaboratorUserLink` — additive only, verify Up/Down.
- [x] 1.5 Unit test (InMemory EF): partial-unique index declared correctly (metadata-level — InMemory provider does not enforce unique-index constraints at runtime, see apply-progress for details); nulls save without error.

## Phase 2: Provisioning (PR 2)

- [x] 2.1 `CollaboratorDtos.cs`: optional `UserId` on create request; `UserId`+`IsLinked` on response; update all `CollaboratorResponse` projections in `CollaboratorService`.
- [x] 2.2 `CollaboratorService.CreateAsync`: resolve `UserId` (explicit → validate Member+unlinked; else email-match unlinked Member; else null); lowercase email on write.
- [x] 2.3 New errors `collaborators.email_taken`, `collaborators.user_already_linked` (Conflict); dup-email check before insert/update.
- [x] 2.4 `GetMeAsync`: match `c.UserId == callerId`, drop the email "first match" fallback.
- [x] 2.5 `AuthService.SignupAsync`: after Member User created, email-match unlinked Collaborator → link; else log warning, no fabricated row.
- [x] 2.6 Integration tests: create w/ UserId (success/409/400), email-match link, no-match pending, dup email 409; signup links roster row or warns; `GET /collaborators/me` returns linked profile or 404 unlinked.

## Phase 3: Reconciliation (PR 3)

- [ ] 3.1 New `CollaboratorReconciliationService.ReconcileAsync`: lowercase emails; match unlinked Collaborator↔Member User 1:1; collect gaps both directions.
- [ ] 3.2 `POST /collaborators/reconcile` endpoint: returns matched count + unlinked-Member-User list.
- [ ] 3.3 `EmployeeConfiguration.cs` add unique `Email` index; `dotnet ef migrations add AddCollaboratorEmailUnique` — apply only post-reconciliation.
- [ ] 3.4 `TestDataSeeder.cs`: realign `member@`/`member2@` Collaborator seeds to match Users, set `UserId`; keep `pedro.castillo@` as sole legacy row.
- [ ] 3.5 Unit tests: match/skip-linked/gap logic; migration test — unique index fails fast on pre-existing dupes.
- [ ] 3.6 Integration test: reconcile links seeded pairs, reports legacy row + any unlinked Member User.

## Phase 4: Reports (PR 4)

- [ ] 4.1 `GetSessionPeriodReport.cs`: `byCollaborator` left-join `Collaborator` on `UserId==CollaboratorId`; name = linked, else User fallback.
- [ ] 4.2 `GetClinicReport.cs`/`GetWorkRouteReport.cs`/`GetSummaryReport.cs`: doc-comment only — roster counts include legacy rows by design, no logic change.
- [ ] 4.3 Integration test: 2-linked+1-legacy seed — session breakdown name resolves; clinic report still counts/names legacy row.
- [ ] 4.4 Integration test: summary vs session-period collaborator population both link-derived; delta documented.
