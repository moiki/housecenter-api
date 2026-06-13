using System.Security.Claims;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.Attachments;

public sealed record ListAttachmentsQuery(
    AttachmentOwnerType OwnerType,
    Guid OwnerId,
    ClaimsPrincipal Principal) : IRequest<Result<IReadOnlyList<AttachmentResponse>>>;

public sealed class ListAttachmentsHandler(HouseCenterDbContext db, IAttachmentAuthorizer authorizer)
    : IRequestHandler<ListAttachmentsQuery, Result<IReadOnlyList<AttachmentResponse>>>
{
    public async Task<Result<IReadOnlyList<AttachmentResponse>>> Handle(
        ListAttachmentsQuery request, CancellationToken ct)
    {
        var auth = await authorizer.AuthorizeAsync(request.OwnerType, request.OwnerId, request.Principal, ct);
        if (auth.IsFailure) return auth.Error;

        var items = await db.FileAttachments
            .AsNoTracking()
            .Where(a => a.OwnerType == request.OwnerType && a.OwnerId == request.OwnerId)
            .OrderByDescending(a => a.CreatedDate)
            .Select(a => new AttachmentResponse(
                a.Id, a.OwnerType.ToString(), a.OwnerId, a.FileName, a.ContentType, a.SizeBytes,
                a.UploadedByUserId, a.CreatedDate, "/attachments/" + a.Id))
            .ToListAsync(ct);

        return items;
    }
}
