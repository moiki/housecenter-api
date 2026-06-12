# SDD Spec — Medical Consultations

## Requirements

### R1 — Open consultation
- Member, Administrator, or Owner can open a consultation
- `PatientId`, `AssignedDoctorId`, `Title` required
- `TreatmentId` optional (may be about general patient condition)
- `AssignedDoctorId` must be in the patient's `AssignedDoctors` list
- Default status: `Open`
- First message body included in the creation request (not a separate call)
- Returns 201 + consultation response

### R2 — List consultations
- Doctors see only consultations where they are `AssignedDoctorId`
- Members see only consultations they opened
- Administrators and Owners see all
- Paginated, ordered by `CreatedDate` descending
- Filter by `Status` supported

### R3 — Post message
- Any participant (opener or assigned doctor) can post a message
- `Body` required, `AttachmentUrl` optional
- Posting a message by the doctor changes status to `UnderReview` (automatic)
- Returns 201 + message response

### R4 — Update status
- The assigned doctor, an Administrator, or the Owner can mark a consultation as `Resolved`
  (matches the access control table in R5)
- `ResolvedAt` is set to `UtcNow` automatically
- Returns 200 + updated consultation

### R5 — Access control summary
| Action | Owner | Admin | Member | Doctor | Sponsor |
|---|---|---|---|---|---|
| Open consultation | ✅ | ✅ | ✅ (own patients) | ✅ | ✗ |
| List consultations | All | All | Own | Assigned | ✗ |
| Post message | ✅ | ✅ | If opener | If assigned | ✗ |
| Resolve | ✅ | ✅ | ✗ | If assigned | ✗ |

## Scenarios

```
SCENARIO: collaborator opens consultation about treatment progress
  GIVEN patient Juan has doctor Rodríguez assigned
  AND member María is Juan's collaborator
  WHEN POST /consultations
       { patientId: juan.id, assignedDoctorId: rodriguez.id,
         title: "Falta de respuesta en extremidad derecha",
         treatmentId: t.id,
         firstMessage: "Llevamos 3 sesiones y no hay mejora en movilidad" }
  THEN 201 is returned, consultation status is "Open"

SCENARIO: doctor replies and consultation moves to UnderReview
  GIVEN consultation is Open
  AND authenticated user is the assigned doctor
  WHEN POST /consultations/{id}/messages
       { body: "Esto es normal en las primeras 4 semanas. Continúen con..." }
  THEN 201 is returned
  AND consultation status changes to "UnderReview" automatically

SCENARIO: doctor resolves the consultation
  WHEN PATCH /consultations/{id}/status { status: "Resolved" }
  THEN consultation status is "Resolved", resolvedAt is set

SCENARIO: sponsor tries to list consultations
  GIVEN user has Sponsor role
  WHEN GET /consultations
  THEN 403 is returned
```

## API surface

```
POST   /consultations                             → 201 ConsultationResponse
GET    /consultations?page=&status=               → 200 PagedResult<ConsultationSummaryResponse>
GET    /consultations/{id}                        → 200 ConsultationDetailResponse (with messages)
POST   /consultations/{id}/messages               → 201 ConsultationMessageResponse
PATCH  /consultations/{id}/status                 → 200 ConsultationResponse
```

## DTOs

```csharp
record CreateConsultationRequest(
    Guid PatientId, Guid? TreatmentId, Guid AssignedDoctorId,
    string Title, string FirstMessage, string? AttachmentUrl);

record ConsultationResponse(
    Guid Id, Guid PatientId, Guid? TreatmentId,
    Guid OpenedByUserId, Guid AssignedDoctorId,
    string Title, string Status, DateTime? ResolvedAt, bool IsActive);

record ConsultationDetailResponse(
    ConsultationResponse Consultation,
    IReadOnlyList<ConsultationMessageResponse> Messages);

record ConsultationMessageResponse(
    Guid Id, Guid ConsultationId, Guid AuthorId, string AuthorName,
    string Body, string? AttachmentUrl, DateTime CreatedDate);

record PostMessageRequest(string Body, string? AttachmentUrl);

record UpdateConsultationStatusRequest(ConsultationStatus Status);
```
