# Design: Patients RBAC Restriction (block Sponsor from clinical data)

## Technical Approach

Every route in `Features/Patients/PatientEndpoints.cs` currently uses bare
`.RequireAuthorization()` (any authenticated user), letting Sponsor read and write individual
clinical records (RNF-029). The fix is a pure authorization-attribute change — no handler,
schema, or migration touch. Apply `PolicyNames.StaffOnly` at each of the 5 group definitions as
a defense-in-depth baseline (Sponsor → 403 everywhere), then tighten writes per-route to match
the sibling `Sessions/SessionEndpoints.cs` precedent: `MemberOrAbove` for create/update,
`AdministratorOrAbove` for destructive/assignment ops, and the existing `OwnerOnly`
doctor-assignment routes left untouched. Reads stay at group `StaffOnly` so Doctor retains the
view access it needs to give remote guidance. This mirrors `ConsultationEndpoints` exactly
(group `StaffOnly` + per-route `MemberOrAbove`), which proves ASP.NET Core layers the two
requirements additively — the more restrictive route policy wins because both must pass.

## Architecture Decisions

### Decision 1 — Layered policy: group `StaffOnly` baseline + per-route tightening

**Choice**: Set every group's `.RequireAuthorization()` → `.RequireAuthorization(PolicyNames.StaffOnly)`,
then add per-route overrides. Full route → policy map (all 5 groups, 25 routes):

| Group | Route | Method | Endpoint name | Target policy |
|-------|-------|--------|---------------|---------------|
| /patients | `/` | GET | ListPatients | **StaffOnly** (group) |
| /patients | `/{id}` | GET | GetPatient | **StaffOnly** (group) |
| /patients | `/{id}/full-summary` | GET | GetPatientFullSummary | **StaffOnly** (group) |
| /patients | `/` | POST | CreatePatient | **MemberOrAbove** |
| /patients | `/{id}` | PUT | UpdatePatient | **MemberOrAbove** |
| /patients | `/{id}` | DELETE | DeactivatePatient | **AdministratorOrAbove** |
| /patients | `/{id}/collaborators/{userId}` | POST | AddPatientCollaborator | **MemberOrAbove** |
| /patients | `/{id}/doctors/{userId}` | POST | AddPatientDoctor | **OwnerOnly** (unchanged) |
| /patients | `/{id}/doctors/{userId}` | DELETE | RemovePatientDoctor | **OwnerOnly** (unchanged) |
| /treatments | `/` | GET | ListTreatments | **StaffOnly** (group) |
| /treatments | `/` | POST | CreateTreatment | **MemberOrAbove** |
| /treatments | `/{treatmentId}` | PUT | UpdateTreatment | **MemberOrAbove** |
| /treatments | `/{treatmentId}/status` | PATCH | UpdateTreatmentStatus | **MemberOrAbove** |
| /treatments | `/{treatmentId}` | DELETE | DeactivateTreatment | **AdministratorOrAbove** |
| /details | `/` | GET | ListTreatmentDetails | **StaffOnly** (group) |
| /details | `/` | POST | CreateTreatmentDetail | **MemberOrAbove** |
| /details | `/{detailId}` | PUT | UpdateTreatmentDetail | **MemberOrAbove** |
| /details | `/{detailId}` | DELETE | DeactivateTreatmentDetail | **AdministratorOrAbove** |
| /tx-comments | `/` | GET | ListTreatmentComments | **StaffOnly** (group) |
| /tx-comments | `/` | POST | CreateTreatmentComment | **MemberOrAbove** |
| /tx-comments | `/{commentId}` | PUT | UpdateTreatmentComment | **MemberOrAbove** |
| /tx-comments | `/{commentId}` | DELETE | DeactivateTreatmentComment | **AdministratorOrAbove** |
| /pt-comments | `/` | POST | CreatePatientComment | **MemberOrAbove** |
| /pt-comments | `/{commentId}` | PUT | UpdatePatientComment | **MemberOrAbove** |
| /pt-comments | `/{commentId}` | DELETE | DeactivatePatientComment | **AdministratorOrAbove** |

(`/pt-comments` has no GET route in the current file — POST/PUT/DELETE only.)

**Alternatives considered**: (a) group `StaffOnly` only, no per-route override — simplest, but
leaves Doctor able to create/delete patients, contradicting the role model; (b) per-route policy
on every route with no group baseline — mirrors bare Sessions group, but a future route added to
the group would silently default to open again. (c) In-handler anonymized projection for Sponsor —
wrong layer (proposal): individual clinical data is a route-level block, aggregate is `/reports`.

**Rationale**: the group `StaffOnly` is a safety net — any future route in these groups inherits a
Sponsor block by default even if a per-route policy is forgotten (this is exactly why
`ConsultationEndpoints` comments *"StaffOnly at group level: sponsors get 403 on every route"*).
Per-route overrides then express the real intent. Both requirements apply additively; the route
policy is always a subset of `StaffOnly`, so it decides the effective access.

### Decision 2 — Write tier: `MemberOrAbove` (create/update/care-team assignment), `AdministratorOrAbove` (delete)

**Choice**: Create/update routes (POST/PUT/PATCH-status), **including** `AddPatientCollaborator`,
→ `MemberOrAbove`; deactivate (DELETE) → `AdministratorOrAbove`.

**Alternatives considered**: keep all writes at `StaffOnly` (lets Doctor do field data entry);
put deletes at `MemberOrAbove` (lets a Member soft-delete any patient record); put
`AddPatientCollaborator` at `AdministratorOrAbove` alongside deletes (this was the design's initial
placement — reconciled during orchestrator diff-pass, see below).

**Rationale**: this is a 1:1 copy of the sibling `SessionEndpoints` precedent — its create (POST)
and update (PATCH status) are `MemberOrAbove`, its DELETE is `AdministratorOrAbove`. Per the role
definitions (`03-casos-de-uso`), **Member** does field data entry, **Doctor** gives remote
guidance through consultations and does *not* enter field data — so writes exclude Doctor
(`MemberOrAbove` = Owner/Administrator/Member). Destructive soft-deletes are management actions
under **Administrator**'s remit (*"manages clinics, routes, collaborators"*), so they rise one
tier to `AdministratorOrAbove`. Note this also tightens **Member** off the delete routes
(previously open to any authenticated user) — a deliberate, precedent-backed hardening, not a
regression.

**Reconciliation note (orchestrator diff-pass, resolving a spec/design divergence)**: the parallel
`sdd-spec` phase placed `AddPatientCollaborator` at `MemberOrAbove`, while this design's first draft
placed it at `AdministratorOrAbove`, both independently flagging it as a judgment call. Resolved in
favor of `MemberOrAbove`: `openspec/changes/housecenter-web/tasks.md:197` ("Assign collaborator
action on Overview tab") and `housecenter-web/proposal.md:128` list this action alongside ordinary
patient-editing on the same tab, with no admin-only gating — it is care-team coordination a Member
does while managing a patient, not an HR/roster action like `POST /collaborators` (which legitimately
is `AdministratorOrAbove`, a different resource entirely). `AddPatientCollaborator` therefore joins
the create/update tier, not the delete tier.

### Decision 3 — Read tier: `StaffOnly`; Doctor keeps read access

**Choice**: All GET routes (list, detail, full-summary, treatment/detail/comment lists) resolve to
group `StaffOnly` with no per-route override.

**Alternatives considered**: `MemberOrAbove` on reads (would block Doctor from viewing patient
records).

**Rationale**: Doctor must read patient data — including the `full-summary` clinical dossier — to
give guidance via consultations. `StaffOnly` (Owner/Administrator/Member/Doctor) removes only
Sponsor, satisfying RNF-029 while keeping every legitimate viewer. Matches `SessionEndpoints` GET
list = `StaffOnly`.

### Decision 4 — `OwnerOnly` doctor-assignment routes: non-regression, no change

**Choice**: Leave `AddPatientDoctor` (POST) and `RemovePatientDoctor` (DELETE) on their existing
`.RequireAuthorization(PolicyNames.OwnerOnly)`.

**Alternatives considered**: none — changing them is out of scope.

**Rationale**: `OwnerOnly ⊂ StaffOnly`, so adding the group `StaffOnly` above them introduces no
conflict and no relaxation — Owner still passes both requirements, all other roles still get 403.
The stricter route policy continues to decide. Explicitly verified: these two routes are unchanged
in behavior after the group-level edit.

## File Changes

| File | Action | Description |
|------|--------|-------------|
| `HouseCenter.Api/Features/Patients/PatientEndpoints.cs` | Modify | 5 groups: bare `.RequireAuthorization()` → `.RequireAuthorization(PolicyNames.StaffOnly)`; add per-route `.RequireAuthorization(MemberOrAbove)` on create/update + `AddPatientCollaborator`, and `.RequireAuthorization(AdministratorOrAbove)` on deactivate routes; leave the two `OwnerOnly` doctor-assignment routes as-is. |
| `HouseCenter.Api.Tests/PatientRbacTests.cs` | Create | New `[Collection("Api")]` integration test class: Sponsor → 403 on every affected route; one Staff role per tier → non-403 (allowed/404/400 as appropriate). |

No new policies, no `Program.cs` change — all four policies already registered
(`Program.cs:150-153`). No entity, DTO, validator, migration, or handler change.

## Interfaces / Contracts

No new interfaces or DTOs. The only contract change is the authorization attribute on each route.
Illustrative diff shape (group + one route of each tier):

```csharp
// group baseline (all 5 groups)
var group = app.MapGroup("/patients").WithTags("Patients")
    .RequireAuthorization(PolicyNames.StaffOnly);   // was: .RequireAuthorization()

// reads inherit group StaffOnly — no override
group.MapGet("/", ...).WithName("ListPatients");

// create/update: field data entry
group.MapPost("/", ...)
    .RequireAuthorization(PolicyNames.MemberOrAbove)
    .WithValidation<CreatePatientRequest>().WithName("CreatePatient");

// deactivate + assignment: management
group.MapDelete("/{id:guid}", ...)
    .RequireAuthorization(PolicyNames.AdministratorOrAbove)
    .WithName("DeactivatePatient");

// unchanged
group.MapPost("/{id:guid}/doctors/{userId:guid}", ...)
    .RequireAuthorization(PolicyNames.OwnerOnly).WithName("AddPatientDoctor");
```

## Testing Strategy

Strict TDD: write failing tests first, then apply the attribute change to make them green.
Model the new `PatientRbacTests` on `ConsultationFlowTests` — reuse its `SeedUserAsync(roleName)` /
`LoginUserWithRoleAsync(roleName)` helpers (copy or lift into a shared base) and the
`GetConsultations_AsSponsor_Returns403` shape (`response.StatusCode.Should().Be(HttpStatusCode.Forbidden)`).

| Layer | What to Test | Approach |
|-------|-------------|----------|
| Integration | **Sponsor → 403** on every affected route (all 5 groups: list, detail, full-summary, create, update, delete, collaborator-assign, treatment/detail/comment CRUD) | WebApplicationFactory; `LoginUserWithRoleAsync(RoleNames.Sponsor)`; assert `Forbidden` |
| Integration | **Doctor → 403 on writes, allowed on reads** (proves Decision 2/3 split) | Doctor client: GET → non-403; POST/PUT/DELETE/collaborator-assign → `Forbidden` |
| Integration | **Member → allowed create/update/collaborator-assign, 403 on deactivate** (proves the two-tier write split) | Member client: POST/PUT/collaborator-assign → success; DELETE → `Forbidden` |
| Integration | **No regression**: existing `PatientFlowTests` (Owner client via `CreateAuthenticatedClientAsync`) stay green — Owner ∈ every tier | Run full suite; Owner passes all policies |
| Integration | `OwnerOnly` doctor-assignment still 403 for Administrator/Member/Doctor, allowed for Owner | Confirms Decision 4 non-regression |

InMemory-EF unit tests are not applicable — this is a routing/authorization concern only
observable through the HTTP pipeline (`WebApplicationFactory`), never at the service layer.

## Migration / Rollout

No data migration, no schema change, no feature flag. Single-file authorization edit plus one new
test file. **Rollback is trivial**: revert the commit — behavior returns to the prior (open) state
with zero data impact.

## Open Questions

- [x] `AddPatientCollaborator` policy tier — **resolved** during the orchestrator's spec/design
  diff-pass to `MemberOrAbove` (see Decision 2's reconciliation note). No longer open.
