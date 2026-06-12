using System.Security.Claims;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Constants;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.Consultations;

public sealed record GetConsultationDetailQuery(
    Guid ConsultationId,
    ClaimsPrincipal Principal) : IRequest<Result<ConsultationDetailResponse>>;

public sealed class GetConsultationDetailHandler(HouseCenterDbContext db)
    : IRequestHandler<GetConsultationDetailQuery, Result<ConsultationDetailResponse>>
{
    public async Task<Result<ConsultationDetailResponse>> Handle(
        GetConsultationDetailQuery request, CancellationToken ct)
    {
        var detail = await db.MedicalConsultations
            .AsNoTracking()
            .Where(c => c.Id == request.ConsultationId)
            .Select(c => new ConsultationDetailResponse(
                new ConsultationResponse(
                    c.Id, c.PatientId, c.TreatmentId, c.OpenedByUserId, c.AssignedDoctorId,
                    c.Title, c.Status.ToString(), c.ResolvedAt, c.IsActive),
                c.Messages
                    .OrderBy(m => m.CreatedDate)
                    .Select(m => new ConsultationMessageResponse(
                        m.Id, m.ConsultationId, m.AuthorId,
                        m.Author.FirstName + " " + m.Author.LastName,
                        m.Body, m.AttachmentUrl, m.CreatedDate))
                    .ToList()))
            .SingleOrDefaultAsync(ct);

        if (detail is null)
            return Error.NotFound("consultations.not_found", "Consultation not found");

        var userId = Guid.Parse(request.Principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var isAdmin = request.Principal.IsInRole(RoleNames.Administrator)
                      || request.Principal.IsInRole(RoleNames.Owner);
        var isParticipant = detail.Consultation.OpenedByUserId == userId
                            || detail.Consultation.AssignedDoctorId == userId;
        if (!isAdmin && !isParticipant)
            return Error.Forbidden("consultations.not_participant",
                "Only participants can view this consultation");

        return detail;
    }
}
