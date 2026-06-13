namespace HouseCenter.Api.Options;

public class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    /// <summary>"local" (default) or "s3". When "s3" the S3 settings below must be set.</summary>
    public string Provider { get; set; } = "local";

    /// <summary>Root directory for LocalDiskFileStorage. Defaults to a folder under the app.</summary>
    public string LocalRootPath { get; set; } = "App_Data/attachments";

    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024; // 10 MB

    public string[] AllowedContentTypes { get; set; } =
        ["image/jpeg", "image/png", "image/webp", "application/pdf"];

    // S3 (Phase 2). ServiceUrl lets the same client target Cloudflare R2 / MinIO.
    public string BucketName { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string? ServiceUrl { get; set; }
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
}
