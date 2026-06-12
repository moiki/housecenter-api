namespace HouseCenter.Api.Domain.Entities;

public class DestinationPoint
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Picture { get; set; }
    public string? GoogleMapUrl { get; set; }
}
