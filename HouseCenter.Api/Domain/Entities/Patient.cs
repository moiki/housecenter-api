namespace HouseCenter.Api.Domain.Entities;

public enum Gender
{
    Male,
    Female
}

public class Patient : BaseEntity
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Profile { get; set; }
    public DateTime BirthDate { get; set; }
    public Gender Gender { get; set; } = Gender.Male;
    public string? Country { get; set; } = "nicaragua";
    public string? State { get; set; }
    public string? City { get; set; }
    public string? Description { get; set; }
    public string Address { get; set; } = string.Empty;
    public AttentionType PrimaryAttentionType { get; set; } = AttentionType.Medical;

    public Guid? ClinicId { get; set; }
    public Clinic? Clinic { get; set; }

    public Guid? WorkRouteId { get; set; }
    public WorkRoute? WorkRoute { get; set; }

    public ICollection<User> Collaborators { get; set; } = [];
    public ICollection<User> AssignedDoctors { get; set; } = [];
    public ICollection<Treatment> Treatments { get; set; } = [];
    public ICollection<PatientComment> Comments { get; set; } = [];
}
