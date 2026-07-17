namespace HouseCenter.Api.Domain.Entities;

public class Collaborator : BaseEntity
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? ProfilePicture { get; set; }
    public string? Country { get; set; } = "nicaragua";
    public string? State { get; set; }
    public string? City { get; set; }
    public string Address { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;

    public Guid ClinicId { get; set; }
    public Clinic Clinic { get; set; } = null!;

    public Guid? WorkRouteId { get; set; }
    public WorkRoute? WorkRoute { get; set; }

    // 1:1 link to the Member User this Collaborator is the field profile of. Null means a
    // legacy/roster-only row (no login), which is a valid permanent state — see
    // openspec/changes/collaborator-user-link/design.md Decision 5.
    public Guid? UserId { get; set; }
    public User? User { get; set; }

    public ICollection<Position> Positions { get; set; } = [];
}
