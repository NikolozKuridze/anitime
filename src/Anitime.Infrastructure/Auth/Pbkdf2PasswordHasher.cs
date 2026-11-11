using System.Security.Cryptography;
using Anitime.Application.Abstractions;

namespace Anitime.Infrastructure.Auth;

public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 600_000; // OWASP recommendation for PBKDF2-HMAC-SHA256

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);

        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public bool NeedsRehash(string hash) =>
        !int.TryParse(hash.AsSpan(0, Math.Max(hash.IndexOf('.'), 0)), out var iterations) || iterations < Iterations;

    public bool Verify(string password, string hash)
    {
        var parts = hash.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations))
        {
            return false;
        }

        var salt = Convert.FromBase64String(parts[1]);
        var expected = Convert.FromBase64String(parts[2]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
