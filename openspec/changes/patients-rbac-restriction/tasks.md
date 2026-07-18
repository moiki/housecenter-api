# Tasks: Patients RBAC Restriction

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~450-650 total (`PatientEndpoints.cs` ~25-30; new `PatientRbacTests.cs` ~420-600) |
| 400-line budget risk | High |
| Chained PRs recommended | Yes |
| Suggested split | PR1 Read tier (StaffOnly) → PR2 Write tier (MemberOrAbove) → PR3 Deactivate tier + OwnerOnly regression |
| Delivery strategy | ask-on-risk |
| Chain strategy | pending — orchestrator to confirm with user |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: pending
400-line budget risk: High

**Basis**: production diff alone is small (~25-30 lines across 5 groups + 17 per-route attributes —
confirmed against the live file). The risk is entirely the new test file. Reference points:
`ConsultationFlowTests.cs` (239 lines / 9 facts, 1 resource) and `PatientFlowTests.cs` (291 lines /
12 facts, partial Patients coverage, no RBAC). `PatientRbacTests.cs` must cover 5 resource groups ×
4 role tiers across 25 routes — realistically larger than either, and plausibly over 400 lines on
its own if written in this codebase's dominant one-Fact-per-scenario style (no `[Theory]` precedent
exists anywhere in the suite). Tasks below direct consolidated/looped Facts per tier to control size,
but even so the total estimate straddles or exceeds the budget — flagging honestly rather than
assuming small.

### Suggested Work Units

| Unit | Goal | Likely PR | Notes |
|------|------|-----------|-------|
| 1 | Read tier: group `StaffOnly` on all 5 groups (blocks Sponsor everywhere — the core RNF-029 fix) + read-tier tests | PR 1 | ~160-210 lines; independently mergeable, delivers the primary fix alone |
| 2 | Write tier: `MemberOrAbove` on 12 create/update/collaborator-assign routes + tests | PR 2 | ~160-210 lines; depends on PR 1's group baseline |
| 3 | Deactivate tier: `AdministratorOrAbove` on 5 deactivate routes + OwnerOnly doctor-assignment regression tests | PR 3 | ~155-235 lines; depends on PR 1 |

## Phase 1: Read tier — StaffOnly (PR 1)

- [x] 1.1 RED: create `HouseCenter.Api.Tests/PatientRbacTests.cs` with shared helpers modeled on
  `ConsultationFlowTests` (`SeedUserAsync`, `LoginUserWithRoleAsync`, `OwnerClient`) plus
  `RunWriteFlowAsync` (patient + treatment + detail + treatment comment + patient comment — doubles
  as the write-tier proof in Phase 2). Add failing tests: Sponsor → 403 on all 6 GET routes (one
  consolidated looped Fact); Owner/Admin/Member/Doctor → 200 on same routes (spec: "Staff roles
  retain read access"). Confirmed RED: `GetRoutes_AsSponsor_Returns403` failed (200 instead of 403).
- [x] 1.2 GREEN: `PatientEndpoints.cs` — all 5 groups' `.RequireAuthorization()` →
  `.RequireAuthorization(PolicyNames.StaffOnly)`. `dotnet test` — new tests pass.
- [x] 1.3 Confirm no regression: existing `PatientFlowTests.cs` (Owner client) stays green (17/17
  passing: 2 new RBAC + 15 existing `PatientFlowTests`).

## Phase 2: Write tier — MemberOrAbove (PR 2)

- [ ] 2.1 RED: add failing tests — Sponsor & Doctor → 403 on all 12 create/update/collaborator-assign
  routes (spec: "Sponsor and Doctor blocked from writing"); Member/Admin/Owner → 200/201 on same
  (spec: "Member retains field data-entry access").
- [ ] 2.2 GREEN: `PatientEndpoints.cs` — add `.RequireAuthorization(PolicyNames.MemberOrAbove)` to the
  12 routes (CreatePatient, UpdatePatient, AddPatientCollaborator, CreateTreatment, UpdateTreatment,
  UpdateTreatmentStatus, CreateTreatmentDetail, UpdateTreatmentDetail, CreateTreatmentComment,
  UpdateTreatmentComment, CreatePatientComment, UpdatePatientComment). `dotnet test` green.

## Phase 3: Deactivate tier + OwnerOnly regression (PR 3)

- [ ] 3.1 RED: add failing tests — Member → 403 on all 5 deactivate routes (spec: "Member blocked
  from deactivating"); Admin/Owner → 200 (spec: "retain deactivate access").
- [ ] 3.2 RED: add regression tests — Owner → 200/201, Administrator → 403 on
  `POST`/`DELETE /patients/{id}/doctors/{userId}` (spec Decision 4 non-regression; expect these to
  pass with zero code change once written).
- [ ] 3.3 GREEN: `PatientEndpoints.cs` — add `.RequireAuthorization(PolicyNames.AdministratorOrAbove)`
  to the 5 deactivate routes. `dotnet test` green; confirm 3.2 passed untouched (proves Decision 4).
- [ ] 3.4 Full regression: run entire `dotnet test` suite — `PatientFlowTests`, `ConsultationFlowTests`,
  `AttentionSessionTests`, `ReportTests` stay green (Owner/Doctor satisfy every tier).
