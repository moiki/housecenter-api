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
        // Fetch-then-map (LOAD-BEARING): the enum->string[] weekday expansion in ToResponse
        // cannot translate to SQL, so this can no longer project inside the EF .Select — load
        // entities (owned JSON Destinations included automatically) then map in memory.
        var workRoutes = await db.WorkRoutes
            .AsNoTracking()
            .Include(w => w.Clinic)
            .OrderBy(w => w.RouteName)
            .ToListAsync(ct);

        return workRoutes.Select(w => ToResponse(w, w.Clinic.Name)).ToList();
    }

    public async Task<Result<WorkRouteResponse>> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var route = await db.WorkRoutes
            .AsNoTracking()
            .Include(w => w.Clinic)
            .SingleOrDefaultAsync(w => w.Id == id, ct);

        return route is not null ? ToResponse(route, route.Clinic.Name) : NotFound;
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
                .ToList(),
            RecurrenceDays = ToFlags(request.RecurrenceDays),
            RecurrenceStartDate = request.RecurrenceStartDate,
            RecurrenceEndDate = request.RecurrenceEndDate,
            IsRecurrenceIndefinite = request.IsRecurrenceIndefinite
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

        // Update allows an empty weekday set (D9) — ToFlags(empty) yields Weekday.None
        route.RecurrenceDays = ToFlags(request.RecurrenceDays);
        route.RecurrenceStartDate = request.RecurrenceStartDate;
        route.RecurrenceEndDate = request.RecurrenceEndDate;
        route.IsRecurrenceIndefinite = request.IsRecurrenceIndefinite;

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
        w.IsActive,
        ToNames(w.RecurrenceDays),
        w.RecurrenceStartDate,
        w.RecurrenceEndDate,
        w.IsRecurrenceIndefinite);

    // Wire format is a PascalCase string[] (e.g. ["Tuesday","Thursday"]) — the [Flags] comma
    // string stays a pure storage detail; these two helpers map at the DTO boundary.
    private static List<string> ToNames(Weekday days) =>
        Enum.GetValues<Weekday>()
            .Where(d => d != Weekday.None && days.HasFlag(d))
            .Select(d => d.ToString())
            .ToList();

    private static Weekday ToFlags(IEnumerable<string> names) =>
        names.Aggregate(Weekday.None, (acc, name) => acc | Enum.Parse<Weekday>(name));
}
