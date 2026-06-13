# SDD Spec — File Uploads

Scope of this spec: **Phase 1 (core + local-disk storage)**. Phase 2 (S3 backend) reuses the
same `IFileStorage` seam and is outlined at the end.

## Requirements

### R1 — Upload
- `POST /attachments?ownerType=&ownerId=` accepts a single multipart file.
- The caller must be authenticated and authorized for that owner (R5); else 403/404.
- The owner entity must exist; unknown owner → 404.
- Validation: size ≤ `MaxFileSizeBytes` (default 10 MB), content-type in the allowlist
  (`image/jpeg`, `image/png`, `image/webp`, `application/pdf`); violations → 400.
- The file is streamed to `IFileStorage` under a generated, collision-free `StorageKey`;
  metadata is persisted. Returns 201 + `AttachmentResponse`.

### R2 — Download
- `GET /attachments/{id}` streams the file with its stored `ContentType`.
- Authorized against the attachment's owner (R5); unauthorized → 404 (don't reveal existence).
- Missing storage object or row → 404.

### R3 — List by owner
- `GET /attachments?ownerType=&ownerId=` returns metadata for that owner's active attachments,
  newest first. Authorized against the owner (R5).

### R4 — Delete
- `DELETE /attachments/{id}` soft-deletes the row (`IsActive=false`) and best-effort removes
  the storage object. Only the uploader or an Administrator/Owner. Returns 204.

### R5 — Authorization (per owner type)
Sponsors are denied for every owner type (attachments are individual data, not aggregate).

| Owner type | Who may upload / read / list |
|---|---|
| `ConsultationMessage` | consultation participant (opener or assigned doctor) or Admin/Owner |
| `AttentionSession` | the session's collaborator or Admin/Owner |
| `Patient` | any staff role (Member/Doctor/Admin/Owner) |
| `Treatment` | same as the treatment's patient |

### R6 — Storage abstraction
- `IFileStorage` with `SaveAsync` / `OpenAsync` / `DeleteAsync` over opaque keys.
- `LocalDiskFileStorage` writes under `FileStorage:LocalRootPath`; the default implementation
  when no S3 config is present. Keys are namespaced (`attachments/{guid}/{safeFileName}`).

## Scenarios

```
SCENARIO: collaborator attaches a photo to a session
  GIVEN member María is the collaborator on session S
  WHEN POST /attachments?ownerType=AttentionSession&ownerId=S  (multipart image/jpeg, 1 MB)
  THEN 201 with attachment id; GET /attachments?ownerType=AttentionSession&ownerId=S lists it

SCENARIO: oversize file rejected
  WHEN POST /attachments ... with a 20 MB file
  THEN 400

SCENARIO: disallowed type rejected
  WHEN POST /attachments ... with content-type application/x-msdownload
  THEN 400

SCENARIO: sponsor cannot read clinical attachments
  GIVEN an attachment on a patient
  WHEN a Sponsor calls GET /attachments/{id}
  THEN 404

SCENARIO: download returns the bytes
  GIVEN an uploaded attachment
  WHEN an authorized user GETs /attachments/{id}
  THEN 200 with the stored content-type and the original bytes

SCENARIO: delete by uploader
  GIVEN María uploaded an attachment
  WHEN María DELETEs /attachments/{id}
  THEN 204; subsequent GET → 404
```

## API surface (Phase 1)

```
POST   /attachments?ownerType=&ownerId=   (multipart: file)  → 201 AttachmentResponse | 400 | 403/404
GET    /attachments/{id}                                     → 200 file stream | 404
GET    /attachments?ownerType=&ownerId=                      → 200 IReadOnlyList<AttachmentResponse>
DELETE /attachments/{id}                                     → 204 | 403/404
```

## DTOs

```csharp
record AttachmentResponse(
    Guid Id, string OwnerType, Guid OwnerId,
    string FileName, string ContentType, long SizeBytes,
    Guid UploadedByUserId, DateTime CreatedDate,
    string DownloadUrl);   // "/attachments/{id}"
```

## AttachmentOwnerType enum
`ConsultationMessage`, `AttentionSession`, `Patient`, `Treatment`.

## Validation rules
| Field | Rule |
|---|---|
| file | required; 1 byte ≤ size ≤ `MaxFileSizeBytes` |
| content-type | in `AllowedContentTypes` |
| `ownerType` | valid `AttachmentOwnerType` |
| `ownerId` | valid Guid; owner must exist |

## Later phase (outline)
- **P2 S3**: `S3FileStorage` over `AmazonS3Client` (bucket + optional `ServiceUrl` for R2/MinIO),
  selected when `FileStorage:Provider=s3` / S3 settings present; `IFileStorage` contract unchanged.
