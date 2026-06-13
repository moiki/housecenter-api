using System.Security.Claims;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Constants;
using HouseCenter.Api.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.Consultations;

public sealed record OpenConsultationCommand(
    Guid PatientId,
    Guid? TreatmentId,
    Guid AssignedDoctorId,
    string Title,
    string FirstMessage,
    string? AttachmentUrl,
    ClaimsPrincipal Principal) : IRequest<Result<ConsultationResponse>>;

public sealed class OpenConsultationHandler(HouseCenterDbContext db)
    : IRequestHandler<OpenConsultationCommand, Result<ConsultationResponse>>
{
    public async Task<Result<ConsultationResponse>> Handle(
        OpenConsultationCommand request, CancellationToken ct)
    {
        var patient = await db.Patients
            .Include(p => p.Collaborators)
            .Include(p => p.AssignedDoctors)
            .SingleOrDefaultAsync(p => p.Id == request.PatientId, ct);
        if (patient is null)
            return Error.NotFound("patients.not_found", "Patient not found");

        var userId = Guid.Parse(request.Principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var isAdmin = request.Principal.IsInRole(RoleNames.Administrator)
                      || request.Principal.IsInRole(RoleNames.Owner);

        // Members can only raise cases for patients they actually attend (spec R5)
        if (!isAdmin && !patient.Collaborators.Any(c => c.Id == userId))
            return Error.Forbidden("consultations.not_patient_collaborator",
                "Only the patient's collaborators can open a consultation");

        if (!patient.AssignedDoctors.Any(d => d.Id == request.AssignedDoctorId))
            return Error.Validation("consultations.doctor_not_assigned",
                "The doctor must be assigned to this patient");

        if (request.TreatmentId is not null &&
            !await db.Treatments.AnyAsync(
                t => t.Id == request.TreatmentId && t.PatientId == request.PatientId, ct))
            return Error.Validation("consultations.treatment_mismatch",
                "Treatment not found or does not belong to this patient");

        var consultation = new MedicalConsultation
        {
            PatientId = request.PatientId,
            TreatmentId = request.TreatmentId,
            OpenedByUserId = userId,
            AssignedDoctorId = request.AssignedDoctorId,
            Title = request.Title,
            Messages =
            [
                new ConsultationMessage
                {
                    AuthorId = userId,
                    Body = request.FirstMessage,
                    AttachmentUrl = request.AttachmentUrl
                }
            ]
        };

        db.MedicalConsultations.Add(consultation);
        await db.SaveChangesAsync(ct);

        return ConsultationMapping.ToResponse(consultation);
    }
}

internal static class ConsultationMapping
{
    internal static ConsultationResponse ToResponse(MedicalConsultation c) => new(
        c.Id, c.PatientId, c.TreatmentId, c.OpenedByUserId, c.AssignedDoctorId,
        c.Title, c.Status.ToString(), c.ResolvedAt, c.IsActive);
}
