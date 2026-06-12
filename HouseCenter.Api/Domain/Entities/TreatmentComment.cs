namespace HouseCenter.Api.Domain.Entities;

public class TreatmentComment : BaseEntity
{
    public string Body { get; set; } = string.Empty;
    public CommentType Type { get; set; } = CommentType.Medical;
    public CommentStatus Status { get; set; } = CommentStatus.Accepted;

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid TreatmentId { get; set; }
    public Treatment Treatment { get; set; } = null!;
}
