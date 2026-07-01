using System.Security.Claims;
using HouseCenter.Api.Common.Email;
using HouseCenter.Api.Common.Pagination;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Entities;
using HouseCenter.Api.Features.Auth;
using HouseCenter.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace HouseCenter.Api.Features.Invitations;

public interface IInvitationService
{
    Task<Result<PagedResult<InvitationResponse>>> ListAsync(PageQuery paging, CancellationToken ct);
    Task<Result<InvitationResponse>> CreateAsync(CreateInvitationRequest request, CancellationToken ct);
    Task<Result> ResendAsync(Guid id, CancellationToken ct);
    Task<Result> DeleteAsync(Guid id, CancellationToken ct);
    Task<Result<InvitationValidationResponse>> ValidateAsync(string token, CancellationToken ct);
}

public sealed class InvitationService(
    HouseCenterDbContext db,
    ITokenService tokenService,
    IEmailService emailService,
    IOptions<AppOptions> appOptions,
    ILogger<InvitationService> logger) : IInvitationService
{
    public async Task<Result<PagedResult<InvitationResponse>>> ListAsync(PageQuery paging, CancellationToken ct)
    {
        var query = db.Invitations
            .AsNoTracking()
            .OrderByDescending(i => i.CreatedDate)
            .Select(i => new InvitationResponse(i.Id, i.Email, i.RoleId, i.Status.ToString(), i.CreatedDate));

        return await query.ToPagedResultAsync(paging, ct);
    }

    public async Task<Result<InvitationResponse>> CreateAsync(CreateInvitationRequest request, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(u => u.Email == request.Email, ct))
            return Error.Conflict("users.email_taken", "A user with this email already exists");

        var pendingExists = await db.Invitations
            .AnyAsync(i => i.Email == request.Email && i.Status == InvitationStatus.Pending && i.IsActive, ct);
        if (pendingExists)
            return Error.Conflict("invitations.already_pending", "A pending invitation for this email already exists");

        var role = await db.Roles.SingleOrDefaultAsync(r => r.Id == request.RoleId, ct);
        if (role is null)
            return Error.NotFound("roles.not_found", "Role not found");

        var invitation = new Invitation { Email = request.Email, RoleId = role.Id };
        db.Invitations.Add(invitation);
        await db.SaveChangesAsync(ct);

        await SendInvitationEmailAsync(invitation, role.Name, ct);

        logger.LogInformation("Invitation created for {Email} with role {Role}", request.Email, role.Name);

        return ToResponse(invitation);
    }

    public async Task<Result> ResendAsync(Guid id, CancellationToken ct)
    {
        var invitation = await db.Invitations.SingleOrDefaultAsync(i => i.Id == id && i.IsActive, ct);
        if (invitation is null)
            return Error.NotFound("invitations.not_found", "Invitation not found");

        if (invitation.Status != InvitationStatus.Pending)
            return Error.Conflict("invitations.not_pending", "Only pending invitations can be resent");

        var role = await db.Roles.SingleAsync(r => r.Id == invitation.RoleId, ct);
        await SendInvitationEmailAsync(invitation, role.Name, ct);

        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct)
    {
        var invitation = await db.Invitations.SingleOrDefaultAsync(i => i.Id == id && i.IsActive, ct);
        if (invitation is null)
            return Error.NotFound("invitations.not_found", "Invitation not found");

        db.Invitations.Remove(invitation);
        await db.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result<InvitationValidationResponse>> ValidateAsync(string token, CancellationToken ct)
    {
        var principal = await tokenService.ValidateAsync(token, TokenKind.Invitation);
        if (principal is null)
            return Error.Unauthorized("invitations.invalid_token", "Invitation token is invalid or expired");

        var invitationId = Guid.Parse(principal.FindFirstValue(TokenService.InvitationIdClaim)!);

        var result = await db.Invitations
            .AsNoTracking()
            .Where(i => i.Id == invitationId && i.IsActive && i.Status == InvitationStatus.Pending)
            .Join(db.Roles, i => i.RoleId, r => r.Id, (i, r) => new InvitationValidationResponse(i.Email, r.Name))
            .SingleOrDefaultAsync(ct);

        return result is not null
            ? result
            : Error.NotFound("invitations.not_found", "Invitation is no longer valid");
    }

    private async Task SendInvitationEmailAsync(Invitation invitation, string roleName, CancellationToken ct)
    {
        var token = tokenService.CreateInvitationToken(invitation);
        var inviteUrl = $"{appOptions.Value.FrontendUrl}/signup?token={Uri.EscapeDataString(token)}";
        await emailService.SendAsync(invitation.Email, "You have been invited to HouseCenter",
            EmailTemplates.Invitation(inviteUrl, roleName), ct);
    }

    private static InvitationResponse ToResponse(Invitation i) =>
        new(i.Id, i.Email, i.RoleId, i.Status.ToString(), i.CreatedDate);
}
