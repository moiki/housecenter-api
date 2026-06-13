using HouseCenter.Api.Domain.Entities;

namespace HouseCenter.Api.Features.Consultations;

public sealed record CreateConsultationRequest(
    Guid PatientId,
    Guid? TreatmentId,
    Guid AssignedDoctorId,
    string Title,
    string FirstMessage,
    string? AttachmentUrl);

public sealed record ConsultationResponse(
    Guid Id,
    Guid PatientId,
    Guid? TreatmentId,
    Guid OpenedByUserId,
    Guid AssignedDoctorId,
    string Title,
    string Status,
    DateTime? ResolvedAt,
    bool IsActive);

public sealed record ConsultationDetailResponse(
    ConsultationResponse Consultation,
    IReadOnlyList<ConsultationMessageResponse> Messages);

public sealed record ConsultationMessageResponse(
    Guid Id,
    Guid ConsultationId,
    Guid AuthorId,
    string AuthorName,
    string Body,
    string? AttachmentUrl,
    DateTime CreatedDate);

public sealed record PostMessageRequest(string Body, string? AttachmentUrl);

public sealed record UpdateConsultationStatusRequest(ConsultationStatus Status);
