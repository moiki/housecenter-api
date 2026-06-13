namespace HouseCenter.Api.Domain.Entities;

public enum NotificationType
{
    ConsultationMessage,
    ConsultationOpened,
    ConsultationResolved,
    SessionUpcoming,
    SessionMissed
}

public class Notification : BaseEntity
{
    public Guid RecipientUserId { get; set; }
    public User Recipient { get; set; } = null!;

    public NotificationType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    // Loose pointer back to the thing that triggered this (e.g. "Consultation" + its id),
    // so a client can deep-link without a hard FK per reference kind.
    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }

    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
}
