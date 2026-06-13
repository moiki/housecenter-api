namespace HouseCenter.Api.Domain.Entities;

public class ConsultationMessage : BaseEntity
{
    public Guid ConsultationId { get; set; }
    public MedicalConsultation Consultation { get; set; } = null!;

    public Guid AuthorId { get; set; }
    public User Author { get; set; } = null!;

    public string Body { get; set; } = string.Empty;
    public string? AttachmentUrl { get; set; }
}
