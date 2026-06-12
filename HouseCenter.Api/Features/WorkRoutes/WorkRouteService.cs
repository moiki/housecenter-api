using HouseCenter.Api.Common.Persistence;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.WorkRoutes;

public interface IWorkRouteService
{
    Task<Result<List<WorkRouteResponse>>> ListAsync(CancellationToken ct);
    Task<Result<WorkRouteResponse>> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Result<WorkRouteResponse>> CreateAsync(CreateWorkRouteRequest request, CancellationToken ct);
    Task<Result<WorkRouteResponse>> UpdateAsync(Guid id, UpdateWorkRouteRequest request, CancellationToken ct);
    Task<Result> DeactivateAsync(Guid id, CancellationToken ct);
}

public sealed class WorkRouteService(
    IRepository<WorkRoute> routes,
    HouseCenterDbContext db) : IWorkRouteService
{
    private static readonly Error NotFound = Error.NotFound("workroutes.not_found", "Work route not found");

    public async Task<Result<List<WorkRouteResponse>>> ListAsync(CancellationToken ct)
    {
        // Owned JSON collection projects like any navigation — EF rebuilds it from the json column
        var list = await db.WorkRoutes
            .AsNoTracking()
            .OrderBy(w => w.RouteName)
            .Select(w => new WorkRouteResponse(
                w.Id, w.RouteName, w.Description, w.FeaturedImage,
                w.ClinicId, w.Clinic.Name,
                w.Destinations
                    .Select(d => new DestinationPointDto(d.Name, d.Description, d.Picture, d.GoogleMapUrl))
                    .ToList(),
                w.IsActive))
            .ToListAsync(ct);

        return list;
    }

    public async Task<Result<WorkRouteResponse>> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var route = await db.WorkRoutes
            .AsNoTracking()
            .Where(w => w.Id == id)
            .Select(w => new WorkRouteResponse(
                w.Id, w.RouteName, w.Description, w.FeaturedImage,
                w.ClinicId, w.Clinic.Name,
                w.Destinations
                    .Select(d => new DestinationPointDto(d.Name, d.Description, d.Picture, d.GoogleMapUrl))
                    .ToList(),
                w.IsActive))
            .SingleOrDefaultAsync(ct);

        return route is not null ? route : NotFound;
    }

    public async Task<Result<WorkRouteResponse>> CreateAsync(CreateWorkRouteRequest request, CancellationToken ct)
    {
        var clinic = await db.Clinics
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == request.ClinicId, ct);
        if (clinic is null)
            return Error.NotFound("clinics.not_found", "Clinic not found");

        var route = new WorkRoute
        {
            RouteName = request.RouteName,
            Description = request.Description,
            FeaturedImage = request.FeaturedImage,
            ClinicId = request.ClinicId,
            Destinations = request.Destinations
                .Select(d => new DestinationPoint
                {
                    Name = d.Name,
                    Description = d.Description,
                    Picture = d.Picture,
                    GoogleMapUrl = d.GoogleMapUrl
                })
                .ToList()
        };

        routes.Add(route);
        await routes.SaveChangesAsync(ct);

        return ToResponse(route, clinic.Name);
    }

    public async Task<Result<WorkRouteResponse>> UpdateAsync(Guid id, UpdateWorkRouteRequest request, CancellationToken ct)
    {
        var route = await routes.Query()
            .Include(w => w.Clinic)
            .SingleOrDefaultAsync(w => w.Id == id, ct);

        if (route is null) return NotFound;

        route.RouteName = request.RouteName;
        route.Description = request.Description;
        route.FeaturedImage = request.FeaturedImage;

        // Owned JSON: no identity per item — the request's list IS the new truth, replace wholesale
        route.Destinations = request.Destinations
            .Select(d => new DestinationPoint
            {
                Name = d.Name,
                Description = d.Description,
                Picture = d.Picture,
                GoogleMapUrl = d.GoogleMapUrl
            })
            .ToList();

        await routes.SaveChangesAsync(ct);

        return ToResponse(route, route.Clinic.Name);
    }

    public async Task<Result> DeactivateAsync(Guid id, CancellationToken ct)
    {
        var route = await routes.GetByIdAsync(id, ct);
        if (route is null) return NotFound;

        route.IsActive = false;
        await routes.SaveChangesAsync(ct);

        return Result.Success();
    }

    private static WorkRouteResponse ToResponse(WorkRoute w, string clinicName) => new(
        w.Id, w.RouteName, w.Description, w.FeaturedImage,
        w.ClinicId, clinicName,
        w.Destinations
            .Select(d => new DestinationPointDto(d.Name, d.Description, d.Picture, d.GoogleMapUrl))
            .ToList(),
        w.IsActive);
}
