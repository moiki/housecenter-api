using System.Security.Claims;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Common.Storage;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Constants;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.Attachments;

public sealed record DeleteAttachmentCommand(Guid Id, ClaimsPrincipal Principal) : IRequest<Result>;

public sealed class DeleteAttachmentHandler(HouseCenterDbContext db, IFileStorage storage)
    : IRequestHandler<DeleteAttachmentCommand, Result>
{
    public async Task<Result> Handle(DeleteAttachmentCommand request, CancellationToken ct)
    {
        var a = await db.FileAttachments.SingleOrDefaultAsync(x => x.Id == request.Id, ct);
        if (a is null) return Error.NotFound("attachments.not_found", "Attachment not found");

        // Stricter than read access: only the uploader or an administrator may delete (R4).
        var userId = Guid.Parse(request.Principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var isAdmin = request.Principal.IsInRole(RoleNames.Owner)
                      || request.Principal.IsInRole(RoleNames.Administrator);
        if (!isAdmin && a.UploadedByUserId != userId)
            return Error.Forbidden("attachments.not_owner", "Only the uploader or an administrator can delete this attachment");

        a.IsActive = false;
        await db.SaveChangesAsync(ct);

        // Best-effort physical removal; the row is already hidden by the soft-delete filter.
        try { await storage.DeleteAsync(a.StorageKey, ct); } catch { /* ignore */ }

        return Result.Success();
    }
}
