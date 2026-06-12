# SDD Tasks — Attention Sessions

## Implementation order

Tasks must be done in order — each depends on the previous.

---

### T1 — Domain: AttentionType enum + Patient/Treatment changes

- [ ] Add `AttentionType` enum to `Domain/Entities/` (Medical, EducationalReinforcement)
- [ ] Add `AttentionType PrimaryAttentionType` to `Patient`
- [ ] Add `AttentionType Type` to `Treatment`
- [ ] Add `ICollection<User> AssignedDoctors` to `Patient`
- [ ] Update `PatientConfiguration` — map `PrimaryAttentionType` as string, configure `patient_doctors` join table
- [ ] Update `TreatmentConfiguration` — map `Type` as string
- [ ] Add migration: `AddAttentionTypeAndDoctorAssignment`

**Acceptance:** `dotnet ef migrations add` succeeds, `dotnet build` clean.

---

### T2 — Domain: AttentionSession entity + configuration

- [ ] Create `AttentionSession : BaseEntity` in `Domain/Entities/`
  ```
  PatientId, Patient, CollaboratorId, Collaborator (User),
  ClinicId?, Clinic?, WorkRouteId?, WorkRoute?,
  AttentionType, SessionDate, DurationMinutes?, Notes?, Status
  ```
- [ ] Add `SessionStatus` enum (Scheduled, Completed, Missed)
- [ ] Create `AttentionSessionConfiguration : IEntityTypeConfiguration<AttentionSession>`
  - enums as string
  - required FKs: PatientId, CollaboratorId
  - nullable FKs: ClinicId, WorkRouteId
- [ ] Add `DbSet<AttentionSession> AttentionSessions` to `HouseCenterDbContext`
- [ ] Add migration: `AddAttentionSessions`
- [ ] Run `dotnet ef database update`

**Acceptance:** migrations apply cleanly, tables exist in DB.

---

### T3 — Update PatientService and DTOs

- [ ] Add `PrimaryAttentionType` to `PatientResponse`, `CreatePatientRequest`, `UpdatePatientRequest`
- [ ] Update `PatientService.CreateAsync` / `UpdateAsync` to map the new field
- [ ] Add `Type` to `TreatmentResponse`, `CreateTreatmentRequest`, `UpdateTreatmentRequest`
- [ ] Update `PatientService.CreateTreatmentAsync` / `UpdateTreatmentAsync`
- [ ] Update validators for the new required fields
- [ ] Add `AddPatientDoctorAsync` / `RemovePatientDoctorAsync` to `IPatientService`

**Acceptance:** `dotnet build` clean, existing tests still pass.

---

### T4 — CQRS handlers: AttentionSessions

Create under `Features/Patients/Sessions/`:

- [ ] `CreateAttentionSession.cs` — command + handler
  - validates ClinicId or WorkRouteId present (service layer check)
  - validates collaborator has Member role
  - adds session, saves
- [ ] `ListAttentionSessions.cs` — query + handler
  - supports `AttentionType?`, `SessionStatus?`, `DateFrom?`, `DateTo?` filters
  - returns `PagedResult<AttentionSessionResponse>`
- [ ] `UpdateSessionStatus.cs` — command + handler
  - validates transition (Scheduled → Completed | Missed only)
  - updates status, optional duration + notes

**Acceptance:** handlers compile, cover the spec scenarios.

---

### T5 — Endpoints

- [ ] Add session endpoints to `PatientEndpoints.cs` (or new `SessionEndpoints.cs`):
  ```
  POST   /patients/{id}/sessions
  GET    /patients/{id}/sessions
  PATCH  /patients/{id}/sessions/{sessionId}/status
  DELETE /patients/{id}/sessions/{sessionId}
  POST   /patients/{id}/doctors/{userId}
  DELETE /patients/{id}/doctors/{userId}
  ```
- [ ] Wire up in `Program.cs`
- [ ] Add validators: `CreateAttentionSessionRequestValidator`

**Acceptance:** `dotnet build` clean.

---

### T6 — Integration tests

- [ ] `AttentionSessionTests.cs`:
  - `CreateSession_WithClinic_Returns201`
  - `CreateSession_WithoutClinicOrRoute_Returns400`
  - `UpdateStatus_ToCompleted_UpdatesDuration`
  - `UpdateStatus_ToMissed_NullDuration`
  - `ListSessions_FilterByType_ReturnsCorrectSubset`
  - `GetSessions_AsSponsor_Returns403`
  - `AddDoctor_ValidDoctorRole_Returns200`
  - `AddDoctor_WithMemberRole_Returns422` (user is not a doctor)

**Acceptance:** all new tests green, existing 39 tests still green.

---

## Estimated effort
4–6 hours (Tasks T1–T6 sequential, no parallel work possible due to DB dependency chain).
