namespace HouseCenter.Api.Common.Storage;

public sealed record FileDownload(Stream Content, string ContentType);

// Storage backend over opaque keys. Implementations: LocalDiskFileStorage (default) and
// S3FileStorage (Phase 2). The rest of the app never knows which is in use.
public interface IFileStorage
{
    Task SaveAsync(string storageKey, Stream content, string contentType, CancellationToken ct = default);

    /// <summary>Opens the stored object, or null if the key is missing.</summary>
    Task<FileDownload?> OpenAsync(string storageKey, CancellationToken ct = default);

    Task DeleteAsync(string storageKey, CancellationToken ct = default);
}
