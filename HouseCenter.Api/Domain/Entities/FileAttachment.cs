namespace HouseCenter.Api.Domain.Entities;

public enum AttachmentOwnerType
{
    ConsultationMessage,
    AttentionSession,
    Patient,
    Treatment
}

// A file attached to any owner via a loose OwnerType + OwnerId pointer (same pattern as
// Notification.ReferenceType/Id) — so no owner entity needs an FK to attachments.
public class FileAttachment : BaseEntity
{
    public AttachmentOwnerType OwnerType { get; set; }
    public Guid OwnerId { get; set; }

    // Opaque key/path inside the storage backend (e.g. "attachments/{guid}/{name}").
    public string StorageKey { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }

    public Guid UploadedByUserId { get; set; }
    public User UploadedBy { get; set; } = null!;
}
