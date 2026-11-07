using System.Security.Cryptography;
using System.Text;
using Anitime.Application.Abstractions;
using Anitime.Application.Common;
using Anitime.Contracts;
using Anitime.Domain.Entities;
using Anitime.Domain.Enums;
using Anitime.Infrastructure.Options;
using Anitime.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Anitime.Api.Controllers;

[Route("api/auth")]
public sealed class AuthController : BaseApiController
{
    [HttpPost("signup")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SignUp(
        [FromBody] SignUpRequest request,
        [FromServices] IValidator<SignUpRequest> validator,
        [FromServices] AppDbContext db,
        [FromServices] IPasswordHasher passwordHasher,
        [FromServices] IJwtTokenService jwtTokenService,
        [FromServices] IOptions<JwtOptions> jwtOptions,
        [FromServices] IClock clock,
        CancellationToken cancellationToken)
    {
        var validationResponse = await ValidateAsync(request, validator, cancellationToken);
        if (validationResponse is not null)
        {
            return validationResponse;
        }

        var email = request.Email.Trim().ToLowerInvariant();
        var exists = await db.Users.AnyAsync(x => x.Email == email, cancellationToken);
        if (exists)
        {
            return FromResult<AuthResponse>(Result<AuthResponse>.Failure(AppErrors.Conflict("Email is already registered.")));
        }

        var now = clock.UtcNow;
        var user = new User
        {
            Email = email,
            Name = request.Name.Trim(),
            Gender = request.Gender,
            DateOfBirth = request.DateOfBirth,
            PasswordHash = passwordHasher.Hash(request.Password),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Settings = new UserSettings
            {
                BackgroundStyleType = BackgroundStyleType.Transparent,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            }
        };

        db.Users.Add(user);
        var authResponse = CreateAuthResponse(db, user, jwtTokenService, jwtOptions.Value, clock.UtcNow);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_users_Email" })
        {
            return FromResult<AuthResponse>(Result<AuthResponse>.Failure(AppErrors.Conflict("Email is already registered.")));
        }

        return FromResult(Result<AuthResponse>.Success(authResponse));
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        [FromServices] IValidator<LoginRequest> validator,
        [FromServices] AppDbContext db,
        [FromServices] IPasswordHasher passwordHasher,
        [FromServices] IJwtTokenService jwtTokenService,
        [FromServices] IOptions<JwtOptions> jwtOptions,
        [FromServices] IClock clock,
        CancellationToken cancellationToken)
    {
        var validationResponse = await ValidateAsync(request, validator, cancellationToken);
        if (validationResponse is not null)
        {
            return validationResponse;
        }

        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(x => x.Email == email, cancellationToken);
        if (user is null || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            return FromResult<AuthResponse>(Result<AuthResponse>.Failure(AppErrors.Unauthorized("Email or password is incorrect.")));
        }

        var authResponse = CreateAuthResponse(db, user, jwtTokenService, jwtOptions.Value, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return FromResult(Result<AuthResponse>.Success(authResponse));
    }

    [HttpPost("refresh")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshTokenRequest request,
        [FromServices] IValidator<RefreshTokenRequest> validator,
        [FromServices] AppDbContext db,
        [FromServices] IJwtTokenService jwtTokenService,
        [FromServices] IOptions<JwtOptions> jwtOptions,
        [FromServices] IClock clock,
        CancellationToken cancellationToken)
    {
        var validationResponse = await ValidateAsync(request, validator, cancellationToken);
        if (validationResponse is not null)
        {
            return validationResponse;
        }

        var now = clock.UtcNow;
        var tokenHash = ComputeTokenHash(request.RefreshToken);
        var refreshToken = await db.RefreshTokens
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);

        if (refreshToken is null || refreshToken.RevokedAtUtc.HasValue || refreshToken.ExpiresAtUtc <= now)
        {
            return FromResult<AuthResponse>(Result<AuthResponse>.Failure(AppErrors.Unauthorized("Refresh token is invalid or expired.")));
        }

        var newRawToken = GenerateRawToken();
        var newTokenHash = ComputeTokenHash(newRawToken);

        refreshToken.RevokedAtUtc = now;
        refreshToken.ReplacedAtUtc = now;
        refreshToken.ReplacedByTokenHash = newTokenHash;
        refreshToken.UpdatedAtUtc = now;

        var nextRefreshToken = new RefreshToken
        {
            UserId = refreshToken.UserId,
            TokenHash = newTokenHash,
            ExpiresAtUtc = now.AddDays(jwtOptions.Value.RefreshTokenDays),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.RefreshTokens.Add(nextRefreshToken);
        refreshToken.User.UpdatedAtUtc = now;
        var (accessToken, accessTokenExpiresAtUtc) = jwtTokenService.Create(refreshToken.User);

        await db.SaveChangesAsync(cancellationToken);
        return FromResult(Result<AuthResponse>.Success(new AuthResponse(
            refreshToken.User.Id,
            refreshToken.User.Email,
            refreshToken.User.Name,
            refreshToken.User.Gender,
            refreshToken.User.DateOfBirth,
            accessToken,
            accessTokenExpiresAtUtc,
            newRawToken,
            nextRefreshToken.ExpiresAtUtc)));
    }

    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Logout(
        [FromBody] LogoutRequest request,
        [FromServices] IValidator<LogoutRequest> validator,
        [FromServices] AppDbContext db,
        [FromServices] IClock clock,
        CancellationToken cancellationToken)
    {
        var validationResponse = await ValidateAsync(request, validator, cancellationToken);
        if (validationResponse is not null)
        {
            return validationResponse;
        }

        var tokenHash = ComputeTokenHash(request.RefreshToken);
        var refreshToken = await db.RefreshTokens.FirstOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);
        if (refreshToken is not null && !refreshToken.RevokedAtUtc.HasValue)
        {
            refreshToken.RevokedAtUtc = clock.UtcNow;
            refreshToken.UpdatedAtUtc = clock.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        return NoContent();
    }

    [HttpPost("forgot-password")]
    [ProducesResponseType(typeof(ForgotPasswordResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request,
        [FromServices] IValidator<ForgotPasswordRequest> validator,
        [FromServices] AppDbContext db,
        [FromServices] IClock clock,
        [FromServices] IWebHostEnvironment environment,
        [FromServices] ILogger<AuthController> logger,
        CancellationToken cancellationToken)
    {
        var validationResponse = await ValidateAsync(request, validator, cancellationToken);
        if (validationResponse is not null)
        {
            return validationResponse;
        }

        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(x => x.Email == email, cancellationToken);
        if (user is null)
        {
            return FromResult(Result<ForgotPasswordResponse>.Success(
                new ForgotPasswordResponse("If the account exists, a reset instruction has been generated.", null)));
        }

        var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
        var now = clock.UtcNow;

        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = tokenHash,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddHours(2)
        });

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Password reset token for {Email}: {Token}", email, rawToken);

        return FromResult(Result<ForgotPasswordResponse>.Success(
            new ForgotPasswordResponse(
                "If the account exists, a reset instruction has been generated.",
                environment.IsDevelopment() ? rawToken : null)));
    }

    [HttpPost("reset-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request,
        [FromServices] IValidator<ResetPasswordRequest> validator,
        [FromServices] AppDbContext db,
        [FromServices] IPasswordHasher passwordHasher,
        [FromServices] IClock clock,
        CancellationToken cancellationToken)
    {
        var validationResponse = await ValidateAsync(request, validator, cancellationToken);
        if (validationResponse is not null)
        {
            return validationResponse;
        }

        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Token)));
        var now = clock.UtcNow;
        var resetToken = await db.PasswordResetTokens
            .Include(x => x.User)
            .FirstOrDefaultAsync(
                x => x.TokenHash == tokenHash && x.UsedAtUtc == null && x.ExpiresAtUtc > now,
                cancellationToken);

        if (resetToken is null)
        {
            return FromResult(Result.Failure(AppErrors.InvalidRequest("Reset token is invalid or expired.")));
        }

        resetToken.User.PasswordHash = passwordHasher.Hash(request.NewPassword);
        resetToken.User.UpdatedAtUtc = now;
        resetToken.UsedAtUtc = now;

        var activeRefreshTokens = await db.RefreshTokens
            .Where(x => x.UserId == resetToken.UserId && x.RevokedAtUtc == null && x.ExpiresAtUtc > now)
            .ToListAsync(cancellationToken);

        foreach (var refreshToken in activeRefreshTokens)
        {
            refreshToken.RevokedAtUtc = now;
            refreshToken.UpdatedAtUtc = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static AuthResponse CreateAuthResponse(
        AppDbContext db,
        User user,
        IJwtTokenService jwtTokenService,
        JwtOptions jwtOptions,
        DateTime now)
    {
        var rawRefreshToken = GenerateRawToken();
        var refreshTokenExpiresAtUtc = now.AddDays(jwtOptions.RefreshTokenDays);

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = ComputeTokenHash(rawRefreshToken),
            ExpiresAtUtc = refreshTokenExpiresAtUtc,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        var (accessToken, accessTokenExpiresAtUtc) = jwtTokenService.Create(user);
        return new AuthResponse(
            user.Id,
            user.Email,
            user.Name,
            user.Gender,
            user.DateOfBirth,
            accessToken,
            accessTokenExpiresAtUtc,
            rawRefreshToken,
            refreshTokenExpiresAtUtc);
    }

    private static string GenerateRawToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private static string ComputeTokenHash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
