namespace Anitime.Infrastructure.Options;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "anitime-api";
    public string Audience { get; set; } = "anitime-clients";
    public string SigningKey { get; set; } = "ChangeThisSuperSecretSigningKeyForProduction123!";
    public int AccessTokenMinutes { get; set; } = 120;
    public int RefreshTokenDays { get; set; } = 90;
}
