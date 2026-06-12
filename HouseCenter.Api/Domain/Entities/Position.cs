namespace HouseCenter.Api.Domain.Entities;

public class Position: BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public Guid EmployeeId { get; set; }
    public Collaborator Collaborator { get; set; } = null!;
}