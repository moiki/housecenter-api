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

    // Weekly-by-weekday recurrence rule (virtual — no occurrence rows are stored;
    // the client expands this rule for the visible calendar range).
    public Weekday RecurrenceDays { get; set; } = Weekday.None;
    public DateOnly RecurrenceStartDate { get; set; }
    public DateOnly? RecurrenceEndDate { get; set; }
    public bool IsRecurrenceIndefinite { get; set; } = true;
}
