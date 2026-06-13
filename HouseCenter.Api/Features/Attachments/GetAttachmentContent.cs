using System.Security.Claims;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Common.Storage;
using HouseCenter.Api.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.Attachments;

public sealed record GetAttachmentContentQuery(Guid Id, ClaimsPrincipal Principal)
    : IRequest<Result<AttachmentContent>>;

public sealed class GetAttachmentContentHandler(
    HouseCenterDbContext db,
    IFileStorage storage,
    IAttachmentAuthorizer authorizer) : IRequestHandler<GetAttachmentContentQuery, Result<AttachmentContent>>
{
    private static readonly Error NotFound = Error.NotFound("attachments.not_found", "Attachment not found");

    public async Task<Result<AttachmentContent>> Handle(GetAttachmentContentQuery request, CancellationToken ct)
    {
        var a = await db.FileAttachments
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.Id, ct);
        if (a is null) return NotFound;

        // Same per-owner check as upload; "no access" comes back as NotFound (R5).
        var auth = await authorizer.AuthorizeAsync(a.OwnerType, a.OwnerId, request.Principal, ct);
        if (auth.IsFailure) return auth.Error;

        var download = await storage.OpenAsync(a.StorageKey, ct);
        if (download is null) return NotFound;

        // Trust the recorded content-type over the backend's guess.
        return new AttachmentContent(download.Content, a.ContentType, a.FileName);
    }
}
