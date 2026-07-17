using System.Security.Claims;
using HouseCenter.Api.Common.Pagination;
using HouseCenter.Api.Common.Persistence;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Constants;
using HouseCenter.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.Collaborators;

public interface ICollaboratorService
{
    Task<Result<PagedResult<CollaboratorResponse>>> ListAsync(PageQuery paging, CancellationToken ct);
    Task<Result<CollaboratorResponse>> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Result<CollaboratorResponse>> GetMeAsync(ClaimsPrincipal principal, CancellationToken ct);
    Task<Result<CollaboratorResponse>> CreateAsync(CreateCollaboratorRequest request, CancellationToken ct);
    Task<Result<CollaboratorResponse>> UpdateAsync(Guid id, UpdateCollaboratorRequest request, CancellationToken ct);
    Task<Result> DeactivateAsync(Guid id, CancellationToken ct);
}

public sealed class CollaboratorService(
    IRepository<Collaborator> collaborators,
    HouseCenterDbContext db) : ICollaboratorService
{
    private static readonly Error NotFound = Error.NotFound("collaborators.not_found", "Collaborator not found");

    public async Task<Result<PagedResult<CollaboratorResponse>>> ListAsync(PageQuery paging, CancellationToken ct)
    {
        var query = db.Collaborators
            .AsNoTracking()
            .OrderBy(c => c.FirstName).ThenBy(c => c.LastName)
            .Select(c => new CollaboratorResponse(
                c.Id, c.FirstName, c.LastName, c.Email, c.PhoneNumber, c.Address,
                c.Country, c.State, c.City, c.ProfilePicture,
                c.ClinicId, c.Clinic.Name, c.WorkRouteId,
                c.Positions.Select(p => new PositionDto(p.Id, p.Name)).ToList(),
                c.IsActive, c.UserId, c.UserId != null));

        return await query.ToPagedResultAsync(paging, ct);
    }

    public async Task<Result<CollaboratorResponse>> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var collaborator = await db.Collaborators
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new CollaboratorResponse(
                c.Id, c.FirstName, c.LastName, c.Email, c.PhoneNumber, c.Address,
                c.Country, c.State, c.City, c.ProfilePicture,
                c.ClinicId, c.Clinic.Name, c.WorkRouteId,
                c.Positions.Select(p => new PositionDto(p.Id, p.Name)).ToList(),
                c.IsActive, c.UserId, c.UserId != null))
            .SingleOrDefaultAsync(ct);

        return collaborator is not null ? collaborator : NotFound;
    }

    public async Task<Result<CollaboratorResponse>> GetMeAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        var callerId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // The partial-unique index on Collaborator.UserId guarantees at most one row per
        // caller, so SingleOrDefaultAsync is safe here (unlike the old email-based lookup,
        // which had no such constraint). See design.md Decision 4.
        var collaborator = await db.Collaborators
            .AsNoTracking()
            .Where(c => c.UserId == callerId)
            .Select(c => new CollaboratorResponse(
                c.Id, c.FirstName, c.LastName, c.Email, c.PhoneNumber, c.Address,
                c.Country, c.State, c.City, c.ProfilePicture,
                c.ClinicId, c.Clinic.Name, c.WorkRouteId,
                c.Positions.Select(p => new PositionDto(p.Id, p.Name)).ToList(),
                c.IsActive, c.UserId, c.UserId != null))
            .SingleOrDefaultAsync(ct);

        return collaborator is not null ? collaborator : NotFound;
    }

    public async Task<Result<CollaboratorResponse>> CreateAsync(CreateCollaboratorRequest request, CancellationToken ct)
    {
        var validation = await ValidateRelationsAsync(request.ClinicId, request.WorkRouteId, ct);
        if (validation.IsFailure) return Result.Failure<CollaboratorResponse>(validation.Error);

        // Emails are normalized to lowercase on every write so a plain unique index (Phase 3,
        // Migration B) gives case-insensitive uniqueness without citext. See design.md Decision 3.
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        if (await db.Collaborators.AnyAsync(c => c.Email.ToLower() == normalizedEmail, ct))
            return Error.Conflict("collaborators.email_taken", "A collaborator with this email already exists");

        var userIdResult = await ResolveUserIdAsync(request.UserId, normalizedEmail, ct);
        if (userIdResult.IsFailure) return Result.Failure<CollaboratorResponse>(userIdResult.Error);

        var collaborator = new Collaborator
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = normalizedEmail,
            PhoneNumber = request.PhoneNumber,
            Address = request.Address,
            Country = request.Country,
            State = request.State,
            City = request.City,
            ProfilePicture = request.ProfilePicture,
            ClinicId = request.ClinicId,
            WorkRouteId = request.WorkRouteId,
            UserId = userIdResult.Value,
            Positions = request.Positions
                .Select(name => new Position { Name = name })
                .ToList()
        };

        collaborators.Add(collaborator);
        await collaborators.SaveChangesAsync(ct);

        return await GetByIdAsync(collaborator.Id, ct);
    }

    public async Task<Result<CollaboratorResponse>> UpdateAsync(Guid id, UpdateCollaboratorRequest request, CancellationToken ct)
    {
        var collaborator = await collaborators.Query()
            .Include(c => c.Positions)
            .SingleOrDefaultAsync(c => c.Id == id, ct);

        if (collaborator is null) return NotFound;

        var validation = await ValidateRelationsAsync(request.ClinicId, request.WorkRouteId, ct);
        if (validation.IsFailure) return Result.Failure<CollaboratorResponse>(validation.Error);

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        if (await db.Collaborators.AnyAsync(c => c.Id != id && c.Email.ToLower() == normalizedEmail, ct))
            return Error.Conflict("collaborators.email_taken", "A collaborator with this email already exists");

        collaborator.FirstName = request.FirstName;
        collaborator.LastName = request.LastName;
        collaborator.Email = normalizedEmail;
        collaborator.PhoneNumber = request.PhoneNumber;
        collaborator.Address = request.Address;
        collaborator.Country = request.Country;
        collaborator.State = request.State;
        collaborator.City = request.City;
        collaborator.ProfilePicture = request.ProfilePicture;
        collaborator.ClinicId = request.ClinicId;
        collaborator.WorkRouteId = request.WorkRouteId;

        // Positions are REAL entities (unlike JSON destinations): clearing the tracked collection
        // marks orphans for DELETE because the FK is required — EF turns this into delete+insert
        collaborator.Positions.Clear();
        foreach (var name in request.Positions)
            collaborator.Positions.Add(new Position { Name = name });

        await collaborators.SaveChangesAsync(ct);

        return await GetByIdAsync(collaborator.Id, ct);
    }

    // Resolution order (design.md Decision 1): (a) explicit UserId -> validate the User
    // exists, has Role=Member, and is not already linked; (b) no UserId -> case-insensitive
    // email-match to an unlinked Role=Member User; (c) no match -> null (pending/legacy row).
    private async Task<Result<Guid?>> ResolveUserIdAsync(Guid? explicitUserId, string normalizedEmail, CancellationToken ct)
    {
        if (explicitUserId is not null)
        {
            var user = await db.Users
                .Include(u => u.Roles)
                .SingleOrDefaultAsync(u => u.Id == explicitUserId, ct);

            if (user is null)
                return Error.NotFound("users.not_found", "User not found");

            if (!user.Roles.Any(r => r.Name == RoleNames.Member))
                return Error.Validation("collaborators.user_not_member", "User must have the Member role to be linked to a Collaborator");

            if (await db.Collaborators.AnyAsync(c => c.UserId == user.Id, ct))
                return Error.Conflict("collaborators.user_already_linked", "This user is already linked to a collaborator");

            return (Guid?)user.Id;
        }

        return await db.Users
            .Where(u => u.Roles.Any(r => r.Name == RoleNames.Member))
            .Where(u => u.Email.ToLower() == normalizedEmail)
            .Where(u => !db.Collaborators.Any(c => c.UserId == u.Id))
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Result> DeactivateAsync(Guid id, CancellationToken ct)
    {
        var collaborator = await collaborators.GetByIdAsync(id, ct);
        if (collaborator is null) return NotFound;

        collaborator.IsActive = false;
        await collaborators.SaveChangesAsync(ct);

        return Result.Success();
    }

    private async Task<Result> ValidateRelationsAsync(Guid clinicId, Guid? workRouteId, CancellationToken ct)
    {
        if (!await db.Clinics.AnyAsync(c => c.Id == clinicId, ct))
            return Error.NotFound("clinics.not_found", "Clinic not found");

        if (workRouteId is not null &&
            !await db.WorkRoutes.AnyAsync(w => w.Id == workRouteId, ct))
            return Error.NotFound("workroutes.not_found", "Work route not found");

        return Result.Success();
    }
}
