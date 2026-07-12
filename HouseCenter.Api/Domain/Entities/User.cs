namespace HouseCenter.Api.Domain.Entities;

public class User : BaseEntity
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? Country { get; set; }
    public string? State { get; set; }
    public string? City { get; set; }
    public string Address { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string HashedPassword { get; set; } = string.Empty;
    public string? Profession { get; set; }
    public int PasswordRecoveryVersion { get; set; } = 0;
    public bool EmailConfirmed { get; set; } = false;
    public bool DarkMode { get; set; } = false;
    public string? ProfilePicture { get; set; }

    public ICollection<Role> Roles { get; set; } = [];
    public ICollection<PatientComment> Comments { get; set; } = [];
    public ICollection<UserSession> Sessions { get; set; } = [];
}
