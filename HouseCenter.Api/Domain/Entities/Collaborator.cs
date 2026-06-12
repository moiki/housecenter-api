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

    public ICollection<Position> Positions { get; set; } = [];
}
