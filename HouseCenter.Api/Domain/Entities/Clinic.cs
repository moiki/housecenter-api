namespace HouseCenter.Api.Domain.Entities;

public class Clinic: BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;

    public ICollection<WorkRoute> WorkRoutes { get; set; } = [];
    public ICollection<Collaborator> Collaborators { get; set; } = [];
    public ICollection<Patient> Patients { get; set; } = [];
}