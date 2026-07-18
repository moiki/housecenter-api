# Spec Delta: Patients RBAC Restriction

Formalizes requirements for proposal `patients-rbac-restriction` (block Sponsor from
individual clinical data). Covers one new capability per the proposal's Capabilities
section; `/reports` (Modified Capabilities: None) is unaffected.

Route inventory verified directly against `Features/Patients/PatientEndpoints.cs` (25
routes across 5 groups) — the proposal's route list undercounted; this spec is exhaustive.

## New Capability: `patients-authorization`

### Requirement: Read routes require Staff role
Every GET route across all 5 patient-domain groups MUST require `PolicyNames.StaffOnly`:
`GET /patients`, `GET /patients/{id}`, `GET /patients/{id}/full-summary`,
`GET /patients/{patientId}/treatments`, `GET /treatments/{treatmentId}/details`,
`GET /treatments/{treatmentId}/comments`. (No GET route exists for `/patients/{id}/comments`
— verified against the live file, not assumed.)

#### Scenario: Sponsor blocked from the most severe clinical exposure
- GIVEN an authenticated user with only the Sponsor role
- WHEN they call `GET /patients/{id}/full-summary`
- THEN the response MUST be `403`, not the patient/treatments/comments dossier

#### Scenario: Staff roles retain read access
- GIVEN an authenticated user with Owner, Administrator, Member, or Doctor role
- WHEN they call any read route listed above
- THEN the response MUST be `200`, unchanged from current behavior

### Requirement: Field data-entry write routes require Member-or-above
`POST`/`PUT`/`PATCH` routes that record clinical field data MUST require
`PolicyNames.MemberOrAbove`: create/update patient, `POST /patients/{id}/collaborators/{userId}`,
create/update/status treatment, create/update treatment detail, create/update treatment
comment, create/update patient comment.

This resolves the proposal's open question (tighten below `StaffOnly`?) by matching two
existing codebase signals: (1) `SessionEndpoints.cs` already scopes `CreateAttentionSession`
and `UpdateAttentionSessionStatus` to `MemberOrAbove`, the same "field data entry" class of
operation; (2) the Doctor role is defined as satisfying `StaffOnly` but explicitly **not**
`MemberOrAbove` — Doctor "no puede... ejecutar las operaciones administrativas" and
participates only in the Consultations module, while Member "registra pacientes,
tratamientos, avances de tratamiento... y comentarios." Doctor gives remote guidance; Doctor
does not enter field data. `MemberOrAbove` is therefore the tightest policy that doesn't
break the Member/Administrator/Owner workflow.

#### Scenario: Sponsor and Doctor blocked from writing patient data
- GIVEN an authenticated user with Sponsor or Doctor role
- WHEN they call `POST /patients`, `PUT /patients/{id}`, or any create/update route listed above
- THEN the response MUST be `403`

#### Scenario: Member retains field data-entry access
- GIVEN an authenticated user with Member, Administrator, or Owner role
- WHEN they call any create/update route listed above
- THEN the response MUST be `200`/`201`, unchanged from current behavior

### Requirement: Destructive (deactivate) write routes require Administrator-or-above
`DELETE` routes that soft-deactivate patient-domain records MUST require
`PolicyNames.AdministratorOrAbove`: deactivate patient, deactivate treatment, deactivate
treatment detail, deactivate treatment comment, deactivate patient comment.

This mirrors the existing convention on the sibling Sessions module, where
`DeleteAttentionSession` is `AdministratorOrAbove` while creation/update of the same
resource is `MemberOrAbove` — destructive removal is reserved above routine field entry.

#### Scenario: Member blocked from deactivating records they can create
- GIVEN an authenticated user with only the Member role
- WHEN they call `DELETE /patients/{id}` or any deactivate route listed above
- THEN the response MUST be `403`

#### Scenario: Administrator and Owner retain deactivate access
- GIVEN an authenticated user with Administrator or Owner role
- WHEN they call any deactivate route listed above
- THEN the response MUST be `200`, unchanged from current behavior

### Requirement: Doctor-assignment routes remain Owner-exclusive (no regression)
`POST /patients/{id}/doctors/{userId}` and `DELETE /patients/{id}/doctors/{userId}` MUST
remain `PolicyNames.OwnerOnly`, unmodified by this change. `StaffOnly`, `MemberOrAbove`, and
`AdministratorOrAbove` all fully contain `OwnerOnly`'s allowed role (Owner satisfies every
policy in this spec), so tightening sibling routes creates no conflict here.

#### Scenario: Owner assigns and removes a doctor unchanged
- GIVEN an authenticated user with the Owner role
- WHEN they call `POST` or `DELETE /patients/{id}/doctors/{userId}`
- THEN the response MUST be unchanged from current behavior (`200`/`201`)

#### Scenario: Administrator remains blocked from doctor assignment
- GIVEN an authenticated user with the Administrator role (not Owner)
- WHEN they call `POST` or `DELETE /patients/{id}/doctors/{userId}`
- THEN the response MUST be `403`, unchanged from current behavior
