using HouseCenter.Api.Options;
using Microsoft.Extensions.Options;

namespace HouseCenter.Api.Common.Storage;

// Default backend: writes files under a root directory. Zero infra for dev/CI. Not for
// multi-replica production (each replica has its own disk) — that's what S3FileStorage is for.
public sealed class LocalDiskFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalDiskFileStorage(IOptions<FileStorageOptions> options)
    {
        _root = Path.GetFullPath(options.Value.LocalRootPath);
        Directory.CreateDirectory(_root);
    }

    public async Task SaveAsync(string storageKey, Stream content, string contentType, CancellationToken ct = default)
    {
        var path = ResolvePath(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = File.Create(path);
        await content.CopyToAsync(file, ct);
    }

    public Task<FileDownload?> OpenAsync(string storageKey, CancellationToken ct = default)
    {
        var path = ResolvePath(storageKey);
        if (!File.Exists(path))
            return Task.FromResult<FileDownload?>(null);

        Stream stream = File.OpenRead(path);
        // ContentType isn't stored on disk; the caller (handler) supplies it from the DB row.
        return Task.FromResult<FileDownload?>(new FileDownload(stream, "application/octet-stream"));
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        var path = ResolvePath(storageKey);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    // Guard against path traversal: the resolved path must stay under the root.
    private string ResolvePath(string storageKey)
    {
        var combined = Path.GetFullPath(Path.Combine(_root, storageKey));
        if (!combined.StartsWith(_root, StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid storage key.");
        return combined;
    }
}
