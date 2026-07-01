using HouseCenter.Api.Domain.Entities;

namespace HouseCenter.Api.Features.Invitations;

public sealed record CreateInvitationRequest(string Email, Guid RoleId);

public sealed record InvitationResponse(
    Guid Id,
    string Email,
    Guid RoleId,
    string Status,
    DateTime CreatedDate);
