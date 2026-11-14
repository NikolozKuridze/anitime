using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Anitime.Application.Abstractions;
using Anitime.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Anitime.Infrastructure.Notes;

/// <summary>
/// HMAC-SHA256 over "{assetId}.{expiry}". The key is derived from the JWT signing key with HKDF, so there is
/// no extra secret to configure, and rotating the JWT key also invalidates every outstanding image URL.
/// </summary>
public sealed class AttachmentUrlSigner : IAttachmentUrlSigner
{
    private static readonly byte[] KeyPurpose = "anitime/note-attachment-url/v1"u8.ToArray();
    private readonly byte[] _key;

    public AttachmentUrlSigner(IOptions<JwtOptions> jwt)
    {
        _key = HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(jwt.Value.SigningKey), 32, salt: null, info: KeyPurpose);
    }

    public string Sign(Guid assetId, long expiresAtUnixSeconds) => Base64Url.EncodeToString(Mac(assetId, expiresAtUnixSeconds));

    public bool IsValid(Guid assetId, long expiresAtUnixSeconds, string? signature, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(signature) || expiresAtUnixSeconds <= now.ToUnixTimeSeconds())
        {
            return false;
        }

        byte[] given;
        try
        {
            given = Base64Url.DecodeFromChars(signature);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(given, Mac(assetId, expiresAtUnixSeconds));
    }

    private byte[] Mac(Guid assetId, long expiresAtUnixSeconds) =>
        HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes($"{assetId:N}.{expiresAtUnixSeconds}"));
}
