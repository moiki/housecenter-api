# SDD Tasks — Medical Consultations

## Prerequisite
`attention-sessions` change must be complete (requires `patient_doctors` join table).

---

### T1 — Domain: ConsultationStatus enum + entities

- [ ] Add `ConsultationStatus` enum (Open, UnderReview, Resolved)
- [ ] Create `MedicalConsultation : BaseEntity`
- [ ] Create `ConsultationMessage : BaseEntity`
- [ ] Create `MedicalConsultationConfiguration`
  - many-to-one: PatientId, OpenedByUserId, AssignedDoctorId, TreatmentId?
  - cascade delete messages when consultation deleted
  - enums as string
- [ ] Create `ConsultationMessageConfiguration`
- [ ] Add `DbSet<MedicalConsultation>`, `DbSet<ConsultationMessage>` to DbContext
- [ ] Migration: `AddMedicalConsultations`
- [ ] `dotnet ef database update`

---

### T2 — CQRS handlers

Create under `Features/Consultations/`:

- [ ] `OpenConsultation.cs` — validates doctor is in patient's AssignedDoctors, creates consultation + first message
- [ ] `ListConsultations.cs` — role-filtered query (Doctor sees own, Member sees own, Admin/Owner sees all)
- [ ] `GetConsultationDetail.cs` — single consultation with messages
- [ ] `PostConsultationMessage.cs` — appends message, auto-transitions to UnderReview if author is the doctor
- [ ] `UpdateConsultationStatus.cs` — resolve only (doctor or admin)

---

### T3 — Endpoints + validators

- [ ] `ConsultationEndpoints.cs` with all 5 routes
- [ ] `ConsultationValidators.cs` — Title (max 300), Body (max 5000), AttachmentUrl (max 500, valid URL if provided)
- [ ] Register service + endpoints in `Program.cs`
- [ ] `ConsultationDtos.cs`

---

### T4 — Integration tests

- [ ] `ConsultationFlowTests.cs`:
  - `OpenConsultation_WithAssignedDoctor_Returns201`
  - `OpenConsultation_WithUnassignedDoctor_Returns422`
  - `PostMessage_ByDoctor_TransitionsToUnderReview`
  - `ResolveConsultation_ByDoctor_SetsResolvedAt`
  - `ListConsultations_AsDoctor_SeesOnlyAssignedOnes`
  - `GetConsultations_AsSponsor_Returns403`

---

## Estimated effort
3–5 hours.
