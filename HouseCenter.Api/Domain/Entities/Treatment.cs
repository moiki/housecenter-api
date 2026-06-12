namespace HouseCenter.Api.Domain.Entities;

public enum TreatmentStatus
{
    Active,
    Completed,
    Paused
}

public class Treatment : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Profile { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public TreatmentStatus Status { get; set; } = TreatmentStatus.Active;

    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;

    public ICollection<TreatmentDetail> Details { get; set; } = [];
    public ICollection<TreatmentComment> Comments { get; set; } = [];
}
