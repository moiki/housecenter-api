# SDD Tasks — Medical Consultations

## Prerequisite
`attention-sessions` change must be complete (requires `patient_doctors` join table).

---

### T1 — Domain: ConsultationStatus enum + entities

- [x] Add `ConsultationStatus` enum (Open, UnderReview, Resolved)
- [x] Create `MedicalConsultation : BaseEntity`
- [x] Create `ConsultationMessage : BaseEntity`
- [x] Create `MedicalConsultationConfiguration`
  - many-to-one: PatientId, OpenedByUserId, AssignedDoctorId, TreatmentId?
  - deletion follows the project's global soft-delete convention (`IsActive` on the
    consultation hides the thread; messages are only reachable through the root).
    FK `ConsultationId` keeps `OnDelete(Cascade)` solely as DB-level safety for hard deletes.
  - enums as string
- [x] Create `ConsultationMessageConfiguration`
- [x] Add `DbSet<MedicalConsultation>`, `DbSet<ConsultationMessage>` to DbContext
- [x] Migration: `AddMedicalConsultations`
- [x] `dotnet ef database update`

---

### T2 — CQRS handlers

Create under `Features/Consultations/`:

- [x] `OpenConsultation.cs` — validates doctor is in patient's AssignedDoctors, creates consultation + first message
- [x] `ListConsultations.cs` — role-filtered query (Doctor sees own, Member sees own, Admin/Owner sees all)
- [x] `GetConsultationDetail.cs` — single consultation with messages
- [x] `PostConsultationMessage.cs` — appends message, auto-transitions to UnderReview if author is the doctor
- [x] `UpdateConsultationStatus.cs` — resolve only (doctor or admin)

---

### T3 — Endpoints + validators

- [x] `ConsultationEndpoints.cs` with all 5 routes
- [x] `ConsultationValidators.cs` — Title (max 300), Body (max 5000), AttachmentUrl (max 500, valid URL if provided)
- [x] Register service + endpoints in `Program.cs`
- [x] `ConsultationDtos.cs`

---

### T4 — Integration tests

- [x] `ConsultationFlowTests.cs`:
  - `OpenConsultation_WithAssignedDoctor_Returns201`
  - `OpenConsultation_WithUnassignedDoctor_Returns400` (Error.Validation → 400, project convention)
  - `PostMessage_ByDoctor_TransitionsToUnderReview`
  - `ResolveConsultation_ByDoctor_SetsResolvedAt`
  - `ListConsultations_AsDoctor_SeesOnlyAssignedOnes`
  - `GetConsultations_AsSponsor_Returns403`
  - `OpenConsultation_AsMemberNotCollaborator_Returns403` (R5: members only open for own patients)
  - `PostMessage_OnResolvedConsultation_Returns409` (R3: resolved threads are closed)

---

## Estimated effort
3–5 hours.
