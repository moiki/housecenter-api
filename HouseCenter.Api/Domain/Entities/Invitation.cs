namespace HouseCenter.Api.Domain.Entities;

public enum InvitationStatus
{
    Pending,
    Accepted,
    Expired
}

public class Invitation : BaseEntity
{
    public string Email { get; set; } = string.Empty;
    public Guid RoleId { get; set; }
    public InvitationStatus Status { get; set; } = InvitationStatus.Pending;
}
