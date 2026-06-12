namespace HouseCenter.Api.Domain.Entities;

public class TreatmentDetail : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Profile { get; set; }
    public DateTime TreatmentDate { get; set; }

    public Guid TreatmentId { get; set; }
    public Treatment Treatment { get; set; } = null!;
}
