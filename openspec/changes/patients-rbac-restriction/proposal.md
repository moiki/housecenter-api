# Proposal: Patients RBAC Restriction (block Sponsor from clinical data)

## Change name
`patients-rbac-restriction`

## Intent

**Security / data-privacy fix — priority.** Every route in the Patients feature module
(`Features/Patients/PatientEndpoints.cs`) applies only bare `.RequireAuthorization()` (any
authenticated user). The **Sponsor** role — which by explicit design (`PolicyNames.StaffOnly`:
*"Every role except Sponsor — sponsors only see aggregate /reports"*) must NEVER see individual
clinical/personal records — can currently read and write the full patient domain. Confirmed live:
a seeded Sponsor gets `200` (not `403`) on patient list/detail, exposing name, birth date, gender,
address, and clinical description of disabled children served by the NGO. `PatientService.ListAsync`/
`GetByIdAsync` take no principal and project every field unconditionally — there is **no compensating
filter**; the missing route policy is the only guard. Tracked as RNF-029.

**Broader than reported.** The two known routes (list, detail) are the tip: **all 5 route groups**
in the file are bare `.RequireAuthorization()`, exposing reads AND writes to Sponsor —
most severely `GET /patients/{id}/full-summary` (complete clinical dossier: patient + treatments +
comments). The doctor-assignment routes are correctly `OwnerOnly` and must not regress. Sibling
`Sessions/SessionEndpoints.cs` is already correctly per-route policied (not affected).

## Scope

### In Scope
- Attach `PolicyNames.StaffOnly` at group level for all patient-data groups in `PatientEndpoints.cs`:
  `/patients`, `/patients/{id}/treatments`, `/treatments/{id}/details`, `/treatments/{id}/comments`,
  `/patients/{id}/comments` — replacing bare `.RequireAuthorization()`.
- Preserve existing stricter `OwnerOnly` on doctor-assignment routes (StaffOnly ⊃ OwnerOnly, no regression).
- Integration tests (WebApplicationFactory) asserting Sponsor → `403` on every patient-data route and
  a Staff role (e.g. Member) → success, mirroring `ConsultationFlowTests.GetConsultations_AsSponsor_Returns403`.

### Out of Scope
- **Frontend fix** (`housecenter-web`: `nav.ts` `roles: ALL_ROLES` → `STAFF_ONLY`, missing `<RequireRole>`
  on `/patients` routes) — recommended follow-up as its own SDD change in that repo's openspec lifecycle.
- Tightening write routes below StaffOnly (e.g. should Doctor create/delete patients?) — flagged for
  sdd-design, not foreclosed.
- Any in-handler anonymized projection for Sponsor (see Approach — wrong layer here).

## Capabilities

### New Capabilities
- `patients-authorization`: role policy required on every patient/treatment/comment route; Sponsor hard-blocked.

### Modified Capabilities
- None. `/reports` (Sponsor's curated aggregate access) is unchanged.

## Approach

Hard 403 block via route policy — matches the established convention. `ConsultationEndpoints`
applies `StaffOnly` at group level; `ReportEndpoints` is explicit: *"Any authenticated role —
including Sponsor. Per-role projection happens inside the handlers."* So the codebase splits
**individual clinical data = route-level block** vs **aggregate = /reports in-handler projection**.
Sponsors already have `/reports` for visibility; Patients needs the block, not anonymization at this layer.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `Features/Patients/PatientEndpoints.cs` | Modified | 5 groups: `.RequireAuthorization()` → `.RequireAuthorization(StaffOnly)` |
| `HouseCenter.Api.Tests/` | New/Modified | RBAC integration tests per role |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| A Staff role loses legitimate access | Low | StaffOnly = all roles except Sponsor; matches prior intent |
| Sessions group's bare group-level auth is a latent gap | Low | Note as hardening; routes already override per-route |
| Frontend still shows Patients nav to Sponsor (dead link → 403) | Med | Out-of-scope follow-up documented |

## Rollback Plan

Single-file, additive policy change — revert the commit to restore prior (open) behavior. No data,
schema, or migration involved.

## Dependencies

- `PolicyNames.StaffOnly` + registration in `Program.cs` (already exist).
- Strict TDD: task breakdown/implementation write failing 403 tests first (`dotnet test`).

## Success Criteria

- [ ] Sponsor receives `403` on every patient/treatment/detail/comment route (read and write).
- [ ] Owner/Administrator/Member/Doctor retain access; `OwnerOnly` doctor-assignment routes unchanged.
- [ ] Integration tests cover Sponsor-blocked and Staff-allowed paths, green under `dotnet test`.
