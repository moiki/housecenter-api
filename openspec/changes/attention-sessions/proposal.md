# SDD Proposal — Attention Sessions

## Change name
`attention-sessions`

## Status
`proposed`

## Problem

The current system records **what was planned** (Treatment) and **medical progress**
(TreatmentDetail), but has no concept of the **care event itself**.

When a collaborator visits a patient — whether at a clinic or via a work route — there
is no record of:
- That the visit happened
- Who attended
- When it occurred
- Whether the patient was present
- What type of care was provided (medical vs educational)

This means the platform cannot answer the questions that matter most to the NGO:
- How many children were attended this month?
- Which patients have not been visited in the last 30 days?
- Which collaborators traveled route X last week?
- How are medical vs educational care cases distributed?

## Proposed change

Introduce an `AttentionSession` entity that records each discrete care event.
Add `AttentionType` (Medical / EducationalReinforcement) to `Patient` and `Treatment`
so sessions can be categorized and filtered by specialty.

## Entities affected

### New: `AttentionSession`
```
Id (Guid)
PatientId (FK → Patient, required)
CollaboratorId (FK → User, required — who attended)
ClinicId (FK → Clinic, nullable — if attended at clinic)
WorkRouteId (FK → WorkRoute, nullable — if visited via route)
AttentionType (Medical | EducationalReinforcement)
SessionDate (DateTime)
DurationMinutes (int, nullable)
Notes (string, nullable)
Status (Scheduled | Completed | Missed)
IsActive / CreatedDate / UpdatedDate (BaseEntity)
```

Constraint: either `ClinicId` or `WorkRouteId` must be set (not both null).
Enforced at service layer, not DB constraint.

### Modified: `Patient`
- Add `AttentionType PrimaryAttentionType` — the main care modality for this patient
- Add `ICollection<User> AssignedDoctors` — doctors who follow this patient remotely

### Modified: `Treatment`
- Add `AttentionType Type` — so a patient with both care types has properly typed treatments

## Scope

- New EF entity + configuration + migration
- CQRS handlers: `CreateSession`, `ListSessions`, `UpdateSessionStatus`
- Endpoints: `POST /patients/{id}/sessions`, `GET /patients/{id}/sessions`,
  `PATCH /patients/{id}/sessions/{sessionId}/status`
- FluentValidation for request DTOs
- Integration tests

## Out of scope

- Scheduling / calendar UI
- Recurrence or bulk session creation
- Notifications on session creation (planned for medical-consultations change)

## Why this is P1

Every other planned feature (medical consultations, sponsor reports) depends on
session data being recorded. Without sessions, the platform has no operational history.
