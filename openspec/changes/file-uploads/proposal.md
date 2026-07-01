# SDD Proposal — File Uploads

## Change name
`file-uploads`

## Status
`implemented` (Phase 1 only — 2026-06-13: core + local-disk storage, all tasks T1–T5
complete, full suite green, merged via PR #8. Phase 2 — S3 backend — remains not started.)

## Problem

`ConsultationMessage.AttachmentUrl` exists but is a dead end: it's a plain string that
demands an already-hosted absolute URL. There is **no way to actually upload a file** —
no storage, no endpoint. Field workers can't attach the photo of a wound, a school report,
or a document from a home visit.

The NGO's reality (rural Nicaragua, low bandwidth) also means files must work against a
local dev/offline setup today and a real object store in production, without rewriting code.

## Proposed change

A generic file-attachment capability:

1. **`FileAttachment` entity** — a polymorphic attachment that points at any owner via
   `OwnerType` + `OwnerId` (same loose-pointer pattern as `Notification.ReferenceType/Id`).
   No schema change to consultation messages, sessions, patients, or treatments — an
   attachment just references them. Supersedes the single `AttachmentUrl`.
2. **`IFileStorage` abstraction** — `LocalDiskFileStorage` (default, zero infra for dev/CI)
   and `S3FileStorage` (AWS S3 / Cloudflare R2 / MinIO via the S3 API), selected by config.
   Same dev-fallback idea as `IEmailService` / `IPushSender`.
3. **Multipart upload through the API** — the client POSTs the file; the API validates
   (size, content-type), streams it to storage, and records metadata. Download streams back
   through the API with per-owner authorization.

## Owner types (what can be attached to)

`ConsultationMessage`, `AttentionSession`, `Patient`, `Treatment`. Authorization reuses each
owner's existing access rules (consultation participant, session collaborator, staff).

## Entities affected

### New: `FileAttachment`
```
Id (Guid)
OwnerType (AttachmentOwnerType enum, stored as string)
OwnerId (Guid)
StorageKey (string — opaque key/path in the backend)
FileName (string — original)
ContentType (string)
SizeBytes (long)
UploadedByUserId (FK → User)
BaseEntity (IsActive soft-delete / CreatedDate / UpdatedDate)
```

### Unchanged
Owner entities are not modified. `ConsultationMessage.AttachmentUrl` stays for backward
compatibility but is superseded by attachments (clients move to the attachment API).

## Delivery plan (chained PRs)

| Phase | Scope | Depends on |
|---|---|---|
| **P1 — Core + local disk** | `IFileStorage` + `LocalDiskFileStorage`; `FileAttachment` + migration; upload/download/list/delete API; per-owner authorization; size + content-type validation; tests | — |
| **P2 — S3 backend** | `S3FileStorage` (AWSSDK.S3) + config-based selection; tests | P1 |

## Scope

- New `FileAttachment` domain + EF config + migration.
- `IFileStorage` + local-disk and S3 implementations + `FileStorageOptions`.
- Upload (multipart), download (stream), list-by-owner, delete endpoints.
- `IAttachmentAuthorizer` enforcing per-owner-type access; sponsors excluded.
- Validation: max size, content-type allowlist.
- Integration tests.

## Out of scope

- Image resizing / thumbnails / EXIF stripping.
- Virus scanning.
- Pre-signed direct-to-storage uploads (revisit once on real object storage).
- Background outbox / async processing.

## Open questions / deferred

- **S3 bucket + credentials** (blocks P2 real use only): bucket, region, optional `ServiceUrl`
  (R2/MinIO), access/secret keys. External setup; until set, storage stays local-disk.

## Why now

Third post-Domain-V2 roadmap item, and the missing half of consultation/session evidence:
the threads and visits exist, but you can't attach the photo that explains them.
