namespace HouseCenter.Api.Features.WorkRoutes;

// A stop is a patient assigned to this route (Patient.WorkRouteId), not a
// separately-entered destination — derived, not stored, so it can never drift out of
// sync with who's actually assigned. Ordered by VisitTime (nulls last) at the query site.
public sealed record WorkRouteStopDto(
    Guid PatientId,
    string PatientName,
    string Address,
    TimeOnly? VisitTime);

public sealed record WorkRouteResponse(
    Guid Id,
    string RouteName,
    string Description,
    string? FeaturedImage,
    Guid ClinicId,
    string ClinicName,
    IReadOnlyList<WorkRouteStopDto> Stops,
    bool IsActive,
    IReadOnlyList<string> RecurrenceDays,
    DateOnly RecurrenceStartDate,
    DateOnly? RecurrenceEndDate,
    bool IsRecurrenceIndefinite);

public sealed record CreateWorkRouteRequest(
    string RouteName,
    string Description,
    string? FeaturedImage,
    Guid ClinicId,
    IReadOnlyList<string> RecurrenceDays,
    DateOnly RecurrenceStartDate,
    DateOnly? RecurrenceEndDate,
    bool IsRecurrenceIndefinite);

public sealed record UpdateWorkRouteRequest(
    string RouteName,
    string Description,
    string? FeaturedImage,
    IReadOnlyList<string> RecurrenceDays,
    DateOnly RecurrenceStartDate,
    DateOnly? RecurrenceEndDate,
    bool IsRecurrenceIndefinite);

public sealed record AssignPatientToRouteRequest(TimeOnly? VisitTime);
