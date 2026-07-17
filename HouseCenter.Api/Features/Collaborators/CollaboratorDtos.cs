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
    bool IsActive,
    // Null == legacy/roster-only row (no linked User) — see
    // openspec/changes/collaborator-user-link/design.md Decision 5. IsLinked is an explicit
    // indicator so callers never have to infer onboarding state from UserId == null.
    Guid? UserId,
    bool IsLinked);

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
    IReadOnlyList<string> Positions,
    // Optional — resolved by CollaboratorService.CreateAsync in this order: (a) explicit
    // UserId -> validate Member + unlinked; (b) null -> case-insensitive email-match to an
    // unlinked Member User; (c) no match -> null (pending/legacy row). See design.md Decision 1.
    Guid? UserId = null);

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
