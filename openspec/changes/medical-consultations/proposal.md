# SDD Proposal — Medical Consultations

## Change name
`medical-consultations`

## Status
`implemented` (2026-06-12 — all tasks T1–T4 complete, 57/57 tests green)

## Problem

The current `TreatmentComment` entity allows collaborators to leave observations on a
treatment, but there is no structured way for a remote doctor to respond. The result is:

- A collaborator writes a concern about a patient's progress → no doctor is notified
- A doctor who reads it has no channel to write back directly
- There's no notion of a case being "under review" or "resolved"
- Any doctor can read any comment regardless of whether they follow that patient

This makes the doctor ↔ collaborator communication effectively one-directional and
invisible to the people who need to act on it.

## Proposed change

Introduce a `MedicalConsultation` entity — a structured case thread:

- Opened by a collaborator or administrator against a specific patient (and optionally
  a specific treatment)
- Assigned to a doctor who follows that patient (from the `patient_doctors` join table
  added in `attention-sessions`)
- Has a clear lifecycle: `Open → UnderReview → Resolved`
- Contains a `ICollection<ConsultationMessage>` — a thread of messages between the
  collaborator and the doctor

This does **not** replace `TreatmentComment` (which remains a simpler observation log).
`MedicalConsultation` is for cases where an answer is needed.

## Entities affected

### New: `MedicalConsultation`
```
Id (Guid)
PatientId (FK → Patient, required)
TreatmentId (FK → Treatment, nullable — consultation may be about the patient in general)
OpenedByUserId (FK → User — collaborator or admin who raised the case)
AssignedDoctorId (FK → User — doctor assigned; must be in patient's AssignedDoctors)
Title (string)
Status (Open | UnderReview | Resolved)
ResolvedAt (DateTime?, nullable)
ICollection<ConsultationMessage> Messages
BaseEntity fields
```

### New: `ConsultationMessage`
```
Id (Guid)
ConsultationId (FK → MedicalConsultation, required)
AuthorId (FK → User)
Body (string)
AttachmentUrl (string?, nullable — for photos or documents from field visits)
BaseEntity fields
```

## Scope

- Two new entities + EF configurations + migration
- CQRS handlers: `OpenConsultation`, `ListConsultations`, `PostMessage`, `UpdateStatus`
- Endpoints under `/consultations` and `/consultations/{id}/messages`
- Role-based access: Doctor sees only consultations for their assigned patients
- Integration tests

## Out of scope

- Real-time notifications (push/email on new message — Phase after this)
- File upload for attachments (URL only — actual upload is infra work)
- Multiple assigned doctors per consultation (one doctor per thread for clarity)

## Why this is P2

Depends on `attention-sessions` for the `patient_doctors` relationship.
Without doctor assignment to patients, there's no valid "assigned doctor" to set here.
