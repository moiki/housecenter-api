namespace HouseCenter.Api.Features.Collaborators;

public sealed record PositionDto(Guid Id, string Name);

public sealed record CollaboratorResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string PhoneNumber,
    string Address,
    string? Country,
    string? State,
    string? City,
    string? ProfilePicture,
    Guid ClinicId,
    string ClinicName,
    Guid? WorkRouteId,
    IReadOnlyList<PositionDto> Positions,
    bool IsActive);

public sealed record CreateCollaboratorRequest(
    string FirstName,
    string LastName,
    string Email,
    string PhoneNumber,
    string Address,
    string? Country,
    string? State,
    string? City,
    string? ProfilePicture,
    Guid ClinicId,
    Guid? WorkRouteId,
    IReadOnlyList<string> Positions);

public sealed record UpdateCollaboratorRequest(
    string FirstName,
    string LastName,
    string Email,
    string PhoneNumber,
    string Address,
    string? Country,
    string? State,
    string? City,
    string? ProfilePicture,
    Guid ClinicId,
    Guid? WorkRouteId,
    IReadOnlyList<string> Positions);
