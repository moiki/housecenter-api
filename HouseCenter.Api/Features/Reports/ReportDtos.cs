namespace HouseCenter.Api.Features.Reports;

public sealed record SummaryReportResponse(
    int TotalPatients,
    int ActivePatients,
    int SessionsThisMonth,
    int Collaborators,
    int Clinics,
    int WorkRoutes,
    int ActiveTreatments,
    IReadOnlyDictionary<string, int> SessionsByAttentionType);

public sealed record ClinicReportResponse(
    Guid ClinicId,
    string ClinicName,
    int PatientsAttending,
    int CollaboratorsAssigned,
    int SessionsThisMonth,
    int ActiveTreatments,
    IReadOnlyList<string>? CollaboratorNames);   // null unless Admin/Owner

public sealed record WorkRouteReportResponse(
    Guid WorkRouteId,
    string RouteName,
    int StopsCount,
    int CollaboratorsOnRoute,
    int SessionsLast30Days,
    int PatientsVisited,
    IReadOnlyList<string>? CollaboratorNames);   // null unless Admin/Owner

public sealed record WeeklySessionBucket(
    DateOnly WeekStart,
    string AttentionType,
    string Status,
    int Count);

public sealed record SessionPeriodReportResponse(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<WeeklySessionBucket> Weeks,
    IReadOnlyDictionary<string, int>? SessionsByCollaborator);   // null unless Admin/Owner
