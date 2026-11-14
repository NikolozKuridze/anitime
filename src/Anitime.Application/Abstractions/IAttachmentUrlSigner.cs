namespace Anitime.Application.Abstractions;

/// <summary>Signs short-lived attachment URLs, so the editor WebView can load a private image without a bearer token.</summary>
public interface IAttachmentUrlSigner
{
    /// <summary>A URL-safe signature over the asset id and expiry (Unix seconds).</summary>
    string Sign(Guid assetId, long expiresAtUnixSeconds);

    /// <summary>True when the signature matches and the expiry hasn't passed.</summary>
    bool IsValid(Guid assetId, long expiresAtUnixSeconds, string? signature, DateTimeOffset now);
}
