# SDD Spec — Attention Sessions

## Requirements

### R1 — Session creation
- A Member or Administrator can create a session for any patient
- `PatientId`, `CollaboratorId`, `AttentionType`, `SessionDate` are required
- Either `ClinicId` or `WorkRouteId` must be provided (validated at service layer)
- Default status: `Scheduled`
- Returns 201 + session response

### R2 — Session list (per patient)
- Any authenticated user can list sessions for a patient
- Response is paginated (`PagedResult<AttentionSessionResponse>`)
- Supports filter by `AttentionType`, `Status`, date range (via query params)
- Ordered by `SessionDate` descending

### R3 — Status update
- Only the collaborator who created the session, or an Administrator, can update status
- Valid transitions: `Scheduled → Completed`, `Scheduled → Missed`
- Completed sessions may include updated `DurationMinutes` and `Notes`
- Returns 200 + updated session

### R4 — Session soft delete
- Administrator only
- Sets `IsActive = false` (global query filter handles exclusion)
- Returns 204

### R5 — AttentionType on Patient
- `PrimaryAttentionType` is required on patient creation
- Existing patients without type: migration default = `Medical`
- Exposed in `PatientResponse`

### R6 — AttentionType on Treatment
- `Type` is required on treatment creation
- Defaults to patient's `PrimaryAttentionType` if not specified
- Exposed in `TreatmentResponse`

### R7 — Doctor assignment to patient
- Owner only can assign/remove doctors from a patient
- `POST /patients/{id}/doctors/{userId}` — add
- `DELETE /patients/{id}/doctors/{userId}` — remove
- User must have Doctor role; validates at service layer
- Returns 200 for both

## Scenarios

```
SCENARIO: collaborator records completed home visit
  GIVEN patient Juan is active and assigned to WorkRoute "Ruta Norte"
  AND collaborator María is authenticated as Member
  WHEN POST /patients/{juan.id}/sessions
       { collaboratorId: maría.id, workRouteId: ruta.id,
         attentionType: "Medical", sessionDate: "2026-06-15",
         status: "Scheduled" }
  THEN 201 is returned with session id and status "Scheduled"

SCENARIO: collaborator marks session as completed
  GIVEN session exists with status "Scheduled"
  AND authenticated user is the session's collaborator
  WHEN PATCH /patients/{id}/sessions/{sessionId}/status
       { status: "Completed", durationMinutes: 45, notes: "Buena sesión" }
  THEN 200 is returned with updated session

SCENARIO: missed session recorded
  GIVEN session exists with status "Scheduled"
  WHEN PATCH ... { status: "Missed" }
  THEN 200 returned; durationMinutes remains null

SCENARIO: sponsor tries to access session list
  GIVEN user has Sponsor role
  WHEN GET /patients/{id}/sessions
  THEN 403 is returned (sponsors use /reports endpoints, not individual sessions)
```

## API surface

```
POST   /patients/{id}/sessions                        → 201 AttentionSessionResponse
GET    /patients/{id}/sessions?page=&pageSize=&type=&status=  → 200 PagedResult<>
PATCH  /patients/{id}/sessions/{sessionId}/status     → 200 AttentionSessionResponse
DELETE /patients/{id}/sessions/{sessionId}            → 204

POST   /patients/{id}/doctors/{userId}                → 200
DELETE /patients/{id}/doctors/{userId}                → 200
```

## DTOs

```csharp
record AttentionSessionResponse(
    Guid Id, Guid PatientId, Guid CollaboratorId, string CollaboratorName,
    Guid? ClinicId, Guid? WorkRouteId, string AttentionType,
    DateTime SessionDate, int? DurationMinutes, string? Notes,
    string Status, bool IsActive);

record CreateAttentionSessionRequest(
    Guid CollaboratorId, Guid? ClinicId, Guid? WorkRouteId,
    AttentionType AttentionType, DateTime SessionDate,
    int? DurationMinutes, string? Notes);

record UpdateSessionStatusRequest(
    SessionStatus Status, int? DurationMinutes, string? Notes);
```

## Validation rules

| Field | Rule |
|---|---|
| `CollaboratorId` | NotEmpty, user must exist and have Member role |
| `ClinicId` / `WorkRouteId` | At least one must be set (custom validator) |
| `SessionDate` | Not in the future by more than 7 days (can pre-schedule near-term) |
| `DurationMinutes` | Between 5 and 480 if provided |
| `Notes` | MaxLength 2000 |
