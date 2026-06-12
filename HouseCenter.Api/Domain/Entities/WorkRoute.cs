namespace HouseCenter.Api.Domain.Entities;

public class WorkRoute : BaseEntity
{
    public string RouteName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? FeaturedImage { get; set; }

    public Guid ClinicId { get; set; }
    public Clinic Clinic { get; set; } = null!;

    public ICollection<DestinationPoint> Destinations { get; set; } = [];
    public ICollection<Collaborator> Collaborators { get; set; } = [];
    public ICollection<Patient> Patients { get; set; } = [];
}
