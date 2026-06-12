namespace HouseCenter.Api.Features.WorkRoutes;

public sealed record DestinationPointDto(
    string Name,
    string Description,
    string? Picture,
    string? GoogleMapUrl);

public sealed record WorkRouteResponse(
    Guid Id,
    string RouteName,
    string Description,
    string? FeaturedImage,
    Guid ClinicId,
    string ClinicName,
    IReadOnlyList<DestinationPointDto> Destinations,
    bool IsActive);

public sealed record CreateWorkRouteRequest(
    string RouteName,
    string Description,
    string? FeaturedImage,
    Guid ClinicId,
    IReadOnlyList<DestinationPointDto> Destinations);

public sealed record UpdateWorkRouteRequest(
    string RouteName,
    string Description,
    string? FeaturedImage,
    IReadOnlyList<DestinationPointDto> Destinations);
