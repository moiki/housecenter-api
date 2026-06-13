# SDD Tasks — File Uploads

Delivered as chained PRs (see proposal). This file details **Phase 1**; Phase 2 expands when it starts.

---

## Phase 1 — Core + local-disk storage

### T1 — Domain: FileAttachment + enum + config + migration
- [ ] `AttachmentOwnerType` enum (ConsultationMessage, AttentionSession, Patient, Treatment)
- [ ] `FileAttachment : BaseEntity` (OwnerType, OwnerId, StorageKey, FileName, ContentType, SizeBytes, UploadedByUserId)
- [ ] `FileAttachmentConfiguration` — enum as string, Restrict on uploader FK, index on (OwnerType, OwnerId)
- [ ] `DbSet<FileAttachment>` + migration `AddFileAttachments` + db update

### T2 — Storage abstraction + local disk
- [ ] `IFileStorage` (SaveAsync / OpenAsync / DeleteAsync) + `FileDownload` record
- [ ] `LocalDiskFileStorage` (writes under FileStorage:LocalRootPath; safe key paths)
- [ ] `FileStorageOptions` (Provider, LocalRootPath, MaxFileSizeBytes, AllowedContentTypes, S3 fields for P2)
- [ ] Register in Program.cs (local-disk default; S3 wired in P2)

### T3 — Authorization
- [ ] `IAttachmentAuthorizer` — per-owner-type access check (R5), sponsors denied, owner-exists check
- [ ] Implementation queries the owner entity and mirrors its existing access rules

### T4 — CQRS + endpoints
- [ ] `UploadAttachment` (validate size/type, authorize, stream to storage, persist)
- [ ] `GetAttachmentContent` (authorize, open stream) + `ListAttachments` + `DeleteAttachment`
- [ ] `AttachmentDtos.cs`; `AttachmentEndpoints.cs` (4 routes; upload `.DisableAntiforgery()`)
- [ ] Register `MapAttachmentEndpoints()` in Program.cs

### T5 — Integration tests
- [ ] `AttachmentTests.cs`:
  - `Upload_ToSession_AsCollaborator_Returns201`
  - `Upload_OversizeFile_Returns400`
  - `Upload_DisallowedContentType_Returns400`
  - `Download_ReturnsStoredBytes`
  - `Download_AsSponsor_Returns404`
  - `List_ByOwner_ReturnsUploaded`
  - `Delete_ByUploader_ThenDownload_Returns404`

**Acceptance:** full suite green.

---

## Phase 2 — S3 backend (expands when PR starts)
- `S3FileStorage` (AWSSDK.S3, bucket + optional ServiceUrl for R2/MinIO); config-based selection; tests.
- Deferred external setup: bucket + credentials via User Secrets → S3 activates, no code change.

---

## Estimated effort
Phase 1: 5–7 hours. Phase 2: 2–3 hours.
