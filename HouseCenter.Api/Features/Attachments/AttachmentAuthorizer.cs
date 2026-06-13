using System.Security.Claims;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Constants;
using HouseCenter.Api.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.Attachments;

public interface IAttachmentAuthorizer
{
    // Success if the caller may upload/read/list attachments for this owner. Returns NotFound
    // both when the owner is missing AND when the caller lacks access — never revealing that a
    // resource they can't see exists. Sponsors are denied for every owner type.
    Task<Result> AuthorizeAsync(
        AttachmentOwnerType ownerType, Guid ownerId, ClaimsPrincipal user, CancellationToken ct);
}

public sealed class AttachmentAuthorizer(HouseCenterDbContext db) : IAttachmentAuthorizer
{
    private static readonly Error Denied = Error.NotFound("attachments.owner_not_found", "Attachment owner not found");

    public async Task<Result> AuthorizeAsync(
        AttachmentOwnerType ownerType, Guid ownerId, ClaimsPrincipal user, CancellationToken ct)
    {
        if (user.IsInRole(RoleNames.Sponsor))
            return Denied; // attachments are individual data; sponsors only see aggregates

        var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var isAdmin = user.IsInRole(RoleNames.Owner) || user.IsInRole(RoleNames.Administrator);

        return ownerType switch
        {
            AttachmentOwnerType.ConsultationMessage => await AuthorizeConsultationMessage(ownerId, userId, isAdmin, ct),
            AttachmentOwnerType.AttentionSession => await AuthorizeSession(ownerId, userId, isAdmin, ct),
            AttachmentOwnerType.Patient => await ExistsThenAllowStaff(db.Patients.AnyAsync(p => p.Id == ownerId, ct)),
            AttachmentOwnerType.Treatment => await ExistsThenAllowStaff(db.Treatments.AnyAsync(t => t.Id == ownerId, ct)),
            _ => Denied
        };
    }

    private async Task<Result> AuthorizeConsultationMessage(Guid messageId, Guid userId, bool isAdmin, CancellationToken ct)
    {
        var c = await db.ConsultationMessages
            .Where(m => m.Id == messageId)
            .Select(m => new { m.Consultation.OpenedByUserId, m.Consultation.AssignedDoctorId })
            .SingleOrDefaultAsync(ct);
        if (c is null) return Denied;
        return isAdmin || c.OpenedByUserId == userId || c.AssignedDoctorId == userId
            ? Result.Success() : Denied;
    }

    private async Task<Result> AuthorizeSession(Guid sessionId, Guid userId, bool isAdmin, CancellationToken ct)
    {
        var collaboratorId = await db.AttentionSessions
            .Where(s => s.Id == sessionId)
            .Select(s => (Guid?)s.CollaboratorId)
            .SingleOrDefaultAsync(ct);
        if (collaboratorId is null) return Denied;
        return isAdmin || collaboratorId == userId ? Result.Success() : Denied;
    }

    // Patient/Treatment: any staff role (sponsors already excluded) may attach, once the owner exists.
    private static async Task<Result> ExistsThenAllowStaff(Task<bool> existsTask) =>
        await existsTask ? Result.Success() : Denied;
}
