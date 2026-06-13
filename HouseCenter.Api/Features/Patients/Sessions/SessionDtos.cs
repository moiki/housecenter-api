using HouseCenter.Api.Domain.Entities;

namespace HouseCenter.Api.Features.Patients.Sessions;

public sealed record AttentionSessionResponse(
    Guid Id,
    Guid PatientId,
    Guid CollaboratorId,
    string CollaboratorName,
    Guid? ClinicId,
    Guid? WorkRouteId,
    string AttentionType,
    DateTime SessionDate,
    int? DurationMinutes,
    string? Notes,
    string Status,
    bool IsActive);

public sealed record CreateAttentionSessionRequest(
    Guid CollaboratorId,
    Guid? ClinicId,
    Guid? WorkRouteId,
    AttentionType AttentionType,
    DateTime SessionDate,
    int? DurationMinutes,
    string? Notes);

public sealed record UpdateSessionStatusRequest(
    SessionStatus Status,
    int? DurationMinutes,
    string? Notes);
