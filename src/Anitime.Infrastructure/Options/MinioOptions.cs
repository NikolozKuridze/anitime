namespace Anitime.Infrastructure.Options;

public sealed class MinioOptions
{
    public const string SectionName = "Minio";

    public string Endpoint { get; set; } = "minio:9000";
    public string AccessKey { get; set; } = "minioadmin";
    public string SecretKey { get; set; } = "minioadmin";
    public string BucketName { get; set; } = "anitime-media";
    public string? PublicBaseUrl { get; set; }
    public bool UseSsl { get; set; }
}
