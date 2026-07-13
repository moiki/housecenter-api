using System.Security.Claims;
using HouseCenter.Api.Common.Pagination;
using HouseCenter.Api.Common.Persistence;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Data;
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
                c.IsActive));

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
                c.IsActive))
            .SingleOrDefaultAsync(ct);

        return collaborator is not null ? collaborator : NotFound;
    }

    public async Task<Result<CollaboratorResponse>> GetMeAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        var userEmail = principal.FindFirstValue(ClaimTypes.Email)!;

        // Case-insensitive match — EF Core translates .ToLower() to SQL LOWER(); Postgres
        // string == is case-sensitive by default. FirstOrDefaultAsync, never
        // SingleOrDefaultAsync — Collaborator.Email has no unique index/constraint, so a
        // duplicate-email row must degrade to "first match," not 500.
        var collaborator = await db.Collaborators
            .AsNoTracking()
            .Where(c => c.Email.ToLower() == userEmail.ToLower())
            .Select(c => new CollaboratorResponse(
                c.Id, c.FirstName, c.LastName, c.Email, c.PhoneNumber, c.Address,
                c.Country, c.State, c.City, c.ProfilePicture,
                c.ClinicId, c.Clinic.Name, c.WorkRouteId,
                c.Positions.Select(p => new PositionDto(p.Id, p.Name)).ToList(),
                c.IsActive))
            .FirstOrDefaultAsync(ct);

        return collaborator is not null ? collaborator : NotFound;
    }

    public async Task<Result<CollaboratorResponse>> CreateAsync(CreateCollaboratorRequest request, CancellationToken ct)
    {
        var validation = await ValidateRelationsAsync(request.ClinicId, request.WorkRouteId, ct);
        if (validation.IsFailure) return Result.Failure<CollaboratorResponse>(validation.Error);

        var collaborator = new Collaborator
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            PhoneNumber = request.PhoneNumber,
            Address = request.Address,
            Country = request.Country,
            State = request.State,
            City = request.City,
            ProfilePicture = request.ProfilePicture,
            ClinicId = request.ClinicId,
            WorkRouteId = request.WorkRouteId,
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

        collaborator.FirstName = request.FirstName;
        collaborator.LastName = request.LastName;
        collaborator.Email = request.Email;
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
