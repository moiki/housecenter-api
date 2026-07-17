namespace HouseCenter.Api.Domain.Entities;

// Not a bool — a third language (or a per-region variant) shouldn't require a schema
// change. Stored as string, matching Gender/PrimaryAttentionType's HasConversion<string>().
public enum Language
{
    En,
    Es
}

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
    public Language Language { get; set; } = Language.Es;
    public string? ProfilePicture { get; set; }

    public ICollection<Role> Roles { get; set; } = [];
    public ICollection<PatientComment> Comments { get; set; } = [];
    public ICollection<UserSession> Sessions { get; set; } = [];

    // Inverse of the 1:1 Collaborator link (see Collaborator.UserId). Null until an admin
    // links this User's field profile via POST /collaborators, or signup auto-links an
    // existing unlinked roster row by email match.
    public Collaborator? Collaborator { get; set; }
}
