using System.Security.Cryptography;
using System.Text;

namespace Anitime.Application.Rules;

/// <summary>The emailed 6-digit password reset code: generation, hashing and checks.</summary>
public static class PasswordResetCode
{
    public const int Length = 6;
    public const int MaxAttempts = 5;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    public static string Generate() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    public static bool IsWellFormed(string? code) => code is { Length: Length } && code.All(char.IsAsciiDigit);

    /// <summary>Salted with the row id, so equal codes for different users never share a hash under the unique index.</summary>
    public static string Hash(Guid tokenId, string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{tokenId:N}:{code}")));

    public static bool Matches(Guid tokenId, string code, string storedHash) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Hash(tokenId, code)),
            Encoding.ASCII.GetBytes(storedHash));
}
