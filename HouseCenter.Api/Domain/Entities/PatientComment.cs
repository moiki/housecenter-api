namespace HouseCenter.Api.Domain.Entities;

public enum CommentType
{
    Route,
    Medical,
    Simple
}

public enum CommentStatus
{
    Pending,
    Accepted,
    Rejected
}

public class PatientComment : BaseEntity
{
    public string Body { get; set; } = string.Empty;
    public CommentType Type { get; set; } = CommentType.Simple;
    public CommentStatus Status { get; set; } = CommentStatus.Pending;

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
}
