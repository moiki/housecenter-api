namespace HouseCenter.Api.Domain.Entities;

public enum ConsultationStatus
{
    Open,
    UnderReview,
    Resolved
}

public class MedicalConsultation : BaseEntity
{
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;

    public Guid? TreatmentId { get; set; }
    public Treatment? Treatment { get; set; }

    public Guid OpenedByUserId { get; set; }
    public User OpenedBy { get; set; } = null!;

    public Guid AssignedDoctorId { get; set; }
    public User AssignedDoctor { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public ConsultationStatus Status { get; set; } = ConsultationStatus.Open;
    public DateTime? ResolvedAt { get; set; }

    public ICollection<ConsultationMessage> Messages { get; set; } = [];
}
