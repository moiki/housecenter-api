# HouseCenter — Domain V2 Implementation Plan

## Context

Phases 1–5 of the migration are complete. The API now has a full foundation:
auth, users, clinics, work routes, collaborators, patients, treatments, comments,
MediatR pipeline, global query filter, pagination, versioning, 39 tests.

This plan covers the three SDD changes that align the platform with its real purpose:
connecting field collaborators, remote doctors, and sponsors around patient care.

---

## Change dependency graph

```
attention-sessions (P1)
    ↓ requires: patient_doctors join table
medical-consultations (P2)
    ↓ requires: session data to be meaningful
sponsor-reports (P3)
```

No parallelism — each change must be complete and in production before the next starts.

---

## Change 1 — Attention Sessions (P1)

**Goal:** Record every care event. Know who attended which patient, when, where, and what type of care.

**Why first:** Every other feature depends on this data existing.

**What changes in the model:**

| What | Detail |
|---|---|
| New `AttentionType` enum | Medical / EducationalReinforcement — added to Patient and Treatment |
| New `patient_doctors` join | Many-to-many User ↔ Patient for remote doctor assignment |
| New `AttentionSession` entity | The core event: who, when, where, what type, what status |
| New `SessionStatus` enum | Scheduled / Completed / Missed |

**Endpoints added:**
```
POST   /patients/{id}/sessions
GET    /patients/{id}/sessions
PATCH  /patients/{id}/sessions/{sessionId}/status
DELETE /patients/{id}/sessions/{sessionId}
POST   /patients/{id}/doctors/{userId}
DELETE /patients/{id}/doctors/{userId}
```

**Learning concepts this change introduces:**
- Custom FluentValidation validator (at least one of ClinicId/WorkRouteId)
- Cross-field validation in FluentValidation (`Must` with access to other properties)
- Status machine pattern at service layer (valid transitions enforced in handler)
- Many-to-many configuration without explicit join entity (EF direct)

**See:** `openspec/changes/attention-sessions/tasks.md` for the full task checklist.

---

## Change 2 — Medical Consultations (P2)

**Goal:** Give remote doctors a structured channel to receive case reports and respond.

**Why second:** Needs `patient_doctors` from Change 1 to validate doctor assignment.

**What changes in the model:**

| What | Detail |
|---|---|
| New `MedicalConsultation` entity | A case thread: patient, doctor, status lifecycle |
| New `ConsultationMessage` entity | Messages within the thread (collaborator ↔ doctor) |
| New `ConsultationStatus` enum | Open / UnderReview / Resolved |

**Endpoints added:**
```
POST   /consultations
GET    /consultations
GET    /consultations/{id}
POST   /consultations/{id}/messages
PATCH  /consultations/{id}/status
```

**Learning concepts this change introduces:**
- Role-filtered queries (same endpoint, different WHERE clause per role)
- Automatic status transitions triggered by business rules (not user input)
- Aggregate root pattern (MedicalConsultation owns its Messages)
- `ClaimsPrincipal` passed through MediatR command for author capture

**See:** `openspec/changes/medical-consultations/tasks.md`

---

## Change 3 — Sponsor Reports (P3)

**Goal:** Give sponsors and administrators an aggregate view of the NGO's impact, without
exposing individual patient clinical records.

**Why third:** Reports over empty data are meaningless. Needs Change 1 data.

**What changes in the model:** No new entities — read-only projections over existing data.

**Endpoints added:**
```
GET    /reports/summary
GET    /reports/clinics/{id}
GET    /reports/work-routes/{id}
GET    /reports/sessions?from=&to=
```

**Learning concepts this change introduces:**
- Read-only CQRS (Query handlers only, all `AsNoTracking`)
- Role-based projection (same query, different DTO shape per role)
- EF grouping queries (`GroupBy` + aggregate functions in LINQ)
- `Task.WhenAll` for parallel aggregation (extend the pattern from Phase 4)

**See:** `openspec/changes/sponsor-reports/tasks.md`

---

## How to start a change

1. Open `openspec/changes/{change-name}/tasks.md`
2. Find the first unchecked task
3. Tell the dotnet-teacher mentor: `"voy a implementar {change-name} — tarea T{N}"`
4. Work through tasks in order — each task has an acceptance criterion
5. Mark tasks complete as you go
6. After all tasks: run `dotnet test` — all tests must be green before moving to next change

---

## What comes after Domain V2

Once these three changes are complete, the platform has:
- Full operational history (sessions)
- A medical communication channel (consultations)
- Sponsor-safe reporting (reports)

Natural next directions (not yet planned):
- **Notifications** — email/push when a consultation receives a new message
- **File uploads** — photos from field visits attached to sessions or consultation messages
- **Patient anonymization** — GDPR-style controls for patient data export/deletion
- **Mobile-friendly API** — response size optimization for low-bandwidth rural connections
- **Frontend integration** — `house-mui-v1` React app consuming this API
