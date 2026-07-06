using HouseCenter.Api.Domain.Entities;

namespace HouseCenter.Api.Features.Patients;

// --- Patient ---
public sealed record PatientResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string? Profile,
    DateTime BirthDate,
    string Gender,
    string? Country,
    string? State,
    string? City,
    string Address,
    string? Description,
    string PrimaryAttentionType,
    Guid? ClinicId,
    Guid? WorkRouteId,
    bool IsActive);

public sealed record CreatePatientRequest(
    string FirstName,
    string LastName,
    string? Profile,
    DateTime BirthDate,
    Gender Gender,
    string? Country,
    string? State,
    string? City,
    string Address,
    string? Description,
    AttentionType PrimaryAttentionType,
    Guid? ClinicId,
    Guid? WorkRouteId);

public sealed record UpdatePatientRequest(
    string FirstName,
    string LastName,
    string? Profile,
    DateTime BirthDate,
    Gender Gender,
    string? Country,
    string? State,
    string? City,
    string Address,
    string? Description,
    AttentionType PrimaryAttentionType,
    Guid? ClinicId,
    Guid? WorkRouteId);

// --- Full summary (4.3) ---
public sealed record PatientFullSummaryResponse(
    PatientResponse Patient,
    IReadOnlyList<TreatmentSummaryDto> Treatments,
    IReadOnlyList<PatientCommentDto> Comments,
    IReadOnlyList<DoctorSummaryDto> AssignedDoctors);

public sealed record TreatmentSummaryDto(
    Guid Id,
    string Name,
    string Description,
    string Status,
    DateTime StartDate,
    DateTime EndDate);

public sealed record PatientCommentDto(
    Guid Id,
    string Body,
    string Type,
    string Status,
    Guid UserId,
    DateTime CreatedDate);

public sealed record DoctorSummaryDto(
    Guid Id,
    string FirstName,
    string LastName,
    string Email);

// --- Treatment ---
public sealed record TreatmentResponse(
    Guid Id,
    Guid PatientId,
    string Name,
    string Description,
    string? Profile,
    string Status,
    string Type,
    DateTime StartDate,
    DateTime EndDate,
    bool IsActive);

// Type is nullable on purpose: when omitted, the service defaults it to the
// patient's PrimaryAttentionType (spec R6).
public sealed record CreateTreatmentRequest(
    string Name,
    string Description,
    string? Profile,
    DateTime StartDate,
    DateTime EndDate,
    AttentionType? Type = null);

// Null Type on update means "leave unchanged".
public sealed record UpdateTreatmentRequest(
    string Name,
    string Description,
    string? Profile,
    DateTime StartDate,
    DateTime EndDate,
    AttentionType? Type = null);

public sealed record UpdateTreatmentStatusRequest(TreatmentStatus Status);

// --- TreatmentDetail ---
public sealed record TreatmentDetailResponse(
    Guid Id,
    Guid TreatmentId,
    string Name,
    string Description,
    string? Profile,
    DateTime TreatmentDate,
    bool IsActive);

public sealed record CreateTreatmentDetailRequest(
    string Name,
    string Description,
    string? Profile,
    DateTime TreatmentDate);

public sealed record UpdateTreatmentDetailRequest(
    string Name,
    string Description,
    string? Profile,
    DateTime TreatmentDate);

// --- TreatmentComment ---
public sealed record TreatmentCommentResponse(
    Guid Id,
    Guid TreatmentId,
    string Body,
    string Type,
    string Status,
    Guid UserId,
    bool IsActive);

public sealed record CreateTreatmentCommentRequest(string Body, CommentType Type);
public sealed record UpdateTreatmentCommentRequest(string Body, CommentType Type, CommentStatus Status);

// --- PatientComment ---
public sealed record PatientCommentResponse(
    Guid Id,
    Guid PatientId,
    string Body,
    string Type,
    string Status,
    Guid UserId,
    bool IsActive);

public sealed record CreatePatientCommentRequest(string Body, CommentType Type);
public sealed record UpdatePatientCommentRequest(string Body, CommentType Type, CommentStatus Status);
