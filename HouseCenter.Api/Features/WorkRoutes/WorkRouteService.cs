using HouseCenter.Api.Common.Pagination;
using HouseCenter.Api.Common.Persistence;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.WorkRoutes;

public interface IWorkRouteService
{
    Task<Result<PagedResult<WorkRouteResponse>>> ListAsync(PageQuery paging, CancellationToken ct);
    Task<Result<WorkRouteResponse>> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Result<WorkRouteResponse>> CreateAsync(CreateWorkRouteRequest request, CancellationToken ct);
    Task<Result<WorkRouteResponse>> UpdateAsync(Guid id, UpdateWorkRouteRequest request, CancellationToken ct);
    Task<Result> DeactivateAsync(Guid id, CancellationToken ct);
    Task<Result<WorkRouteResponse>> AssignPatientAsync(Guid routeId, Guid patientId, AssignPatientToRouteRequest request, CancellationToken ct);
    Task<Result<WorkRouteResponse>> UnassignPatientAsync(Guid routeId, Guid patientId, CancellationToken ct);
}

public sealed class WorkRouteService(
    IRepository<WorkRoute> routes,
    HouseCenterDbContext db) : IWorkRouteService
{
    private static readonly Error NotFound = Error.NotFound("workroutes.not_found", "Work route not found");

    public async Task<Result<PagedResult<WorkRouteResponse>>> ListAsync(PageQuery paging, CancellationToken ct)
    {
        // Fetch-then-map (LOAD-BEARING): the enum->string[] weekday expansion in ToResponse
        // cannot translate to SQL, so this can no longer project inside the EF .Select — paginate
        // the ENTITY query first (Skip/Take over WorkRoute), THEN map .Items to WorkRouteResponse
        // in memory, preserving Page/PageSize/TotalCount from the entity-level paged result.
        var entityPage = await db.WorkRoutes
            .AsNoTracking()
            .Include(w => w.Clinic)
            .Include(w => w.Patients)
            .OrderBy(w => w.RouteName)
            .ToPagedResultAsync(paging, ct);

        var items = entityPage.Items.Select(w => ToResponse(w, w.Clinic.Name)).ToList();

        return new PagedResult<WorkRouteResponse>(items, entityPage.Page, entityPage.PageSize, entityPage.TotalCount);
    }

    public async Task<Result<WorkRouteResponse>> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var route = await db.WorkRoutes
            .AsNoTracking()
            .Include(w => w.Clinic)
            .Include(w => w.Patients)
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
            .Include(w => w.Patients)
            .SingleOrDefaultAsync(w => w.Id == id, ct);

        if (route is null) return NotFound;

        route.RouteName = request.RouteName;
        route.Description = request.Description;
        route.FeaturedImage = request.FeaturedImage;

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

    // Reassigning (a patient already on another route) is allowed on purpose — moving a
    // patient between routes is a normal thing to do, not an error.
    public async Task<Result<WorkRouteResponse>> AssignPatientAsync(
        Guid routeId, Guid patientId, AssignPatientToRouteRequest request, CancellationToken ct)
    {
        if (!await db.WorkRoutes.AnyAsync(w => w.Id == routeId, ct))
            return NotFound;

        var patient = await db.Patients.SingleOrDefaultAsync(p => p.Id == patientId, ct);
        if (patient is null)
            return Error.NotFound("patients.not_found", "Patient not found");

        patient.WorkRouteId = routeId;
        patient.RouteVisitTime = request.VisitTime;
        await db.SaveChangesAsync(ct);

        return await GetByIdAsync(routeId, ct);
    }

    public async Task<Result<WorkRouteResponse>> UnassignPatientAsync(Guid routeId, Guid patientId, CancellationToken ct)
    {
        if (!await db.WorkRoutes.AnyAsync(w => w.Id == routeId, ct))
            return NotFound;

        var patient = await db.Patients.SingleOrDefaultAsync(p => p.Id == patientId && p.WorkRouteId == routeId, ct);
        if (patient is null)
            return Error.NotFound("patients.not_on_route", "Patient is not assigned to this route");

        patient.WorkRouteId = null;
        patient.RouteVisitTime = null;
        await db.SaveChangesAsync(ct);

        return await GetByIdAsync(routeId, ct);
    }

    private static WorkRouteResponse ToResponse(WorkRoute w, string clinicName) => new(
        w.Id, w.RouteName, w.Description, w.FeaturedImage,
        w.ClinicId, clinicName,
        // Stops = patients assigned to this route (Patient.WorkRouteId), ordered by visit
        // time — nulls (no time set yet) sort last rather than first.
        w.Patients
            .OrderBy(p => p.RouteVisitTime ?? TimeOnly.MaxValue)
            .Select(p => new WorkRouteStopDto(p.Id, $"{p.FirstName} {p.LastName}", p.Address, p.RouteVisitTime))
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
