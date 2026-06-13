using System.Security.Claims;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Common.Storage;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Entities;
using HouseCenter.Api.Options;
using MediatR;
using Microsoft.Extensions.Options;

namespace HouseCenter.Api.Features.Attachments;

public sealed record UploadAttachmentCommand(
    AttachmentOwnerType OwnerType,
    Guid OwnerId,
    Stream Content,
    string FileName,
    string ContentType,
    long SizeBytes,
    ClaimsPrincipal Principal) : IRequest<Result<AttachmentResponse>>;

public sealed class UploadAttachmentHandler(
    HouseCenterDbContext db,
    IFileStorage storage,
    IAttachmentAuthorizer authorizer,
    IOptions<FileStorageOptions> options) : IRequestHandler<UploadAttachmentCommand, Result<AttachmentResponse>>
{
    public async Task<Result<AttachmentResponse>> Handle(UploadAttachmentCommand request, CancellationToken ct)
    {
        var opts = options.Value;

        if (request.SizeBytes <= 0)
            return Error.Validation("attachments.empty", "File is empty");
        if (request.SizeBytes > opts.MaxFileSizeBytes)
            return Error.Validation("attachments.too_large",
                $"File exceeds the maximum size of {opts.MaxFileSizeBytes} bytes");
        if (!opts.AllowedContentTypes.Contains(request.ContentType))
            return Error.Validation("attachments.unsupported_type",
                $"Content type '{request.ContentType}' is not allowed");

        var auth = await authorizer.AuthorizeAsync(request.OwnerType, request.OwnerId, request.Principal, ct);
        if (auth.IsFailure) return auth.Error;

        var userId = Guid.Parse(request.Principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // Key namespaces by a fresh guid so original names never collide; the filename is
        // sanitized to a leaf only (no path separators) to keep the key flat and safe.
        var safeName = Path.GetFileName(request.FileName);
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "file";
        var storageKey = $"attachments/{Guid.NewGuid():N}/{safeName}";

        await storage.SaveAsync(storageKey, request.Content, request.ContentType, ct);

        var attachment = new FileAttachment
        {
            OwnerType = request.OwnerType,
            OwnerId = request.OwnerId,
            StorageKey = storageKey,
            FileName = safeName,
            ContentType = request.ContentType,
            SizeBytes = request.SizeBytes,
            UploadedByUserId = userId
        };
        db.FileAttachments.Add(attachment);
        await db.SaveChangesAsync(ct);

        return AttachmentMapping.ToResponse(attachment);
    }
}

internal static class AttachmentMapping
{
    public static AttachmentResponse ToResponse(FileAttachment a) => new(
        a.Id, a.OwnerType.ToString(), a.OwnerId, a.FileName, a.ContentType, a.SizeBytes,
        a.UploadedByUserId, a.CreatedDate, $"/attachments/{a.Id}");
}
