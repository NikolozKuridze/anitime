using Anitime.Domain.Enums;

namespace Anitime.Contracts;

public sealed record UpdateProfileRequest(string Name, Gender Gender, DateOnly? DateOfBirth);

public sealed record UpdateUserSettingsRequest(
    BackgroundStyleType BackgroundStyleType,
    string? BackgroundPrimaryColor,
    string? BackgroundSecondaryColor,
    string? AccentColor,
    string? Locale,
    string? TimeZone);

public sealed record UserSettingsResponse(
    BackgroundStyleType BackgroundStyleType,
    string? BackgroundPrimaryColor,
    string? BackgroundSecondaryColor,
    string? AccentColor,
    string? Locale,
    string? TimeZone);

public sealed record UserProfileResponse(
    Guid UserId,
    string Email,
    string Name,
    Gender Gender,
    DateOnly? DateOfBirth,
    int? Age,
    string? ProfileImageUrl,
    string? BackgroundImageUrl,
    UserSettingsResponse Settings);
