namespace HouseCenter.Api.Features.Attachments;

public sealed record AttachmentResponse(
    Guid Id,
    string OwnerType,
    Guid OwnerId,
    string FileName,
    string ContentType,
    long SizeBytes,
    Guid UploadedByUserId,
    DateTime CreatedDate,
    string DownloadUrl);

// Carries the opened stream back to the endpoint for streaming the download.
public sealed record AttachmentContent(Stream Stream, string ContentType, string FileName);
