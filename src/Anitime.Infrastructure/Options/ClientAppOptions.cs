namespace Anitime.Infrastructure.Options;

/// <summary>
/// Oldest app builds the server still supports. Set <c>ClientApp__MinIosBuild</c> / <c>ClientApp__MinAppVersion</c>
/// (docker-compose: MIN_IOS_BUILD / MIN_APP_VERSION). The defaults block nothing.
/// </summary>
public sealed class ClientAppOptions
{
    public const string SectionName = "ClientApp";

    public int MinIosBuild { get; set; }

    public string MinAppVersion { get; set; } = "0.0.0";
}
