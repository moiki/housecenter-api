# SDD Tasks — Attention Sessions

## Implementation order

Tasks must be done in order — each depends on the previous.

---

### T1 — Domain: AttentionType enum + Patient/Treatment changes

- [x] Add `AttentionType` enum to `Domain/Entities/` (Medical, EducationalReinforcement)
- [x] Add `AttentionType PrimaryAttentionType` to `Patient`
- [x] Add `AttentionType Type` to `Treatment`
- [x] Add `ICollection<User> AssignedDoctors` to `Patient`
- [x] Update `PatientConfiguration` — map `PrimaryAttentionType` as string, configure `patient_doctors` join table
- [x] Update `TreatmentConfiguration` — map `Type` as string
- [x] Add migration: `AddAttentionTypeAndDoctorAssignment`

**Acceptance:** `dotnet ef migrations add` succeeds, `dotnet build` clean.

---

### T2 — Domain: AttentionSession entity + configuration

- [x] Create `AttentionSession : BaseEntity` in `Domain/Entities/`
  ```
  PatientId, Patient, CollaboratorId, Collaborator (User),
  ClinicId?, Clinic?, WorkRouteId?, WorkRoute?,
  AttentionType, SessionDate, DurationMinutes?, Notes?, Status
  ```
- [x] Add `SessionStatus` enum (Scheduled, Completed, Missed)
- [x] Create `AttentionSessionConfiguration : IEntityTypeConfiguration<AttentionSession>`
  - enums as string
  - required FKs: PatientId, CollaboratorId
  - nullable FKs: ClinicId, WorkRouteId
- [x] Add `DbSet<AttentionSession> AttentionSessions` to `HouseCenterDbContext`
- [x] Add migration: `AddAttentionSessions`
- [x] Run `dotnet ef database update`

**Acceptance:** migrations apply cleanly, tables exist in DB.

---

### T3 — Update PatientService and DTOs

- [x] Add `PrimaryAttentionType` to `PatientResponse`, `CreatePatientRequest`, `UpdatePatientRequest`
- [x] Update `PatientService.CreateAsync` / `UpdateAsync` to map the new field
- [x] Add `Type` to `TreatmentResponse`, `CreateTreatmentRequest`, `UpdateTreatmentRequest`
- [x] Update `PatientService.CreateTreatmentAsync` / `UpdateTreatmentAsync`
- [x] Update validators for the new required fields
- [x] Add `AddPatientDoctorAsync` / `RemovePatientDoctorAsync` to `IPatientService`

**Acceptance:** `dotnet build` clean, existing tests still pass.

---

### T4 — CQRS handlers: AttentionSessions

Create under `Features/Patients/Sessions/`:

- [x] `CreateAttentionSession.cs` — command + handler
  - validates ClinicId or WorkRouteId present (service layer check)
  - validates collaborator has Member role
  - adds session, saves
- [x] `ListAttentionSessions.cs` — query + handler
  - supports `AttentionType?`, `SessionStatus?`, `DateFrom?`, `DateTo?` filters
  - returns `PagedResult<AttentionSessionResponse>`
- [x] `UpdateSessionStatus.cs` — command + handler
  - validates transition (Scheduled → Completed | Missed only)
  - updates status, optional duration + notes
- [x] `DeleteAttentionSession.cs` — command + handler
  - Administrator only (spec R4)
  - soft delete: sets `IsActive = false` (global query filter excludes it)

**Acceptance:** handlers compile, cover the spec scenarios.

---

### T5 — Endpoints

- [x] Add session endpoints to `PatientEndpoints.cs` (or new `SessionEndpoints.cs`):
  ```
  POST   /patients/{id}/sessions
  GET    /patients/{id}/sessions
  PATCH  /patients/{id}/sessions/{sessionId}/status
  DELETE /patients/{id}/sessions/{sessionId}
  POST   /patients/{id}/doctors/{userId}
  DELETE /patients/{id}/doctors/{userId}
  ```
- [x] Wire up in `Program.cs`
- [x] Add validators: `CreateAttentionSessionRequestValidator`

**Acceptance:** `dotnet build` clean.

---

### T6 — Integration tests

- [x] `AttentionSessionTests.cs`:
  - `CreateSession_WithClinic_Returns201`
  - `CreateSession_WithoutClinicOrRoute_Returns400`
  - `UpdateStatus_ToCompleted_UpdatesDuration`
  - `UpdateStatus_ToMissed_NullDuration`
  - `UpdateStatus_OnCompletedSession_Returns409` (status machine: terminal states are final)
  - `ListSessions_FilterByType_ReturnsCorrectSubset`
  - `GetSessions_AsSponsor_Returns403`
  - `AddDoctor_ValidDoctorRole_Returns204` (project convention: empty success → 204)
  - `AddDoctor_WithMemberRole_Returns400` (user is not a doctor — Error.Validation → 400)
  - `RemoveDoctor_AfterAssign_Returns204`

**Acceptance:** all new tests green, existing 39 tests still green.

---

## Estimated effort
4–6 hours (Tasks T1–T6 sequential, no parallel work possible due to DB dependency chain).
