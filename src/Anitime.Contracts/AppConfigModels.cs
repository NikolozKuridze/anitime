namespace Anitime.Contracts;

/// <param name="MinIosBuild">An iOS build number (CFBundleVersion) below this must update before using the app; 0 blocks nothing.</param>
/// <param name="MinAppVersion">A marketing version (CFBundleShortVersionString) below this must update; "0.0.0" blocks nothing.</param>
public sealed record AppConfigResponse(int MinIosBuild, string MinAppVersion);
