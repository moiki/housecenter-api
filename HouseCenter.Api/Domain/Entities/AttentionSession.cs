namespace HouseCenter.Api.Domain.Entities;

public enum SessionStatus
{
    Scheduled,
    Completed,
    Missed
}

public class AttentionSession : BaseEntity
{
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;

    public Guid CollaboratorId { get; set; }
    public User Collaborator { get; set; } = null!;

    public Guid? ClinicId { get; set; }
    public Clinic? Clinic { get; set; }

    public Guid? WorkRouteId { get; set; }
    public WorkRoute? WorkRoute { get; set; }

    public AttentionType AttentionType { get; set; }
    public DateTime SessionDate { get; set; }
    public int? DurationMinutes { get; set; }
    public string? Notes { get; set; }
    public SessionStatus Status { get; set; } = SessionStatus.Scheduled;
}
