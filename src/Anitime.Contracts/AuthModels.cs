using Anitime.Domain.Enums;

namespace Anitime.Contracts;

public sealed record SignUpRequest(string Email, string Password, string Name, Gender Gender, DateOnly? DateOfBirth);

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshTokenRequest(string RefreshToken);

public sealed record LogoutRequest(string RefreshToken);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(string Token, string NewPassword);

public sealed record AuthResponse(
    Guid UserId,
    string Email,
    string Name,
    Gender Gender,
    DateOnly? DateOfBirth,
    string AccessToken,
    DateTime ExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc);

public sealed record ForgotPasswordResponse(string Message, string? DebugToken);
