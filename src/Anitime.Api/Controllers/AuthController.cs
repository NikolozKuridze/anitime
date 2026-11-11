using System.Security.Cryptography;
using System.Text;
using Anitime.Api.Infrastructure;
using Anitime.Application.Abstractions;
using Anitime.Application.Common;
using Anitime.Application.Rules;
using Anitime.Contracts;
using Anitime.Domain.Entities;
using Anitime.Domain.Enums;
using Anitime.Infrastructure.Options;
using Anitime.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Anitime.Api.Controllers;

[Route("api/auth")]
public sealed class AuthController : BaseApiController
{
    // A client whose refresh response was lost, or two requests racing, must not be logged out by rotation.
    private static readonly TimeSpan RefreshReuseGrace = TimeSpan.FromSeconds(30);

    private const string ForgotPasswordMessage = "If an account exists for this email, we sent a 6-digit code to it.";
    private const string InvalidResetCodeMessage = "This code is wrong or has expired. Request a new one.";

    [HttpPost("signup")]
    [EnableRateLimiting(AuthRateLimiting.PolicyName)]
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
    [EnableRateLimiting(AuthRateLimiting.PolicyName)]
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
        if (user is null)
        {
            // Same work as a real check, so response time doesn't reveal which emails are registered.
            _ = passwordHasher.Hash(request.Password);
            return FromResult<AuthResponse>(Result<AuthResponse>.Failure(AppErrors.Unauthorized("Email or password is incorrect.")));
        }

        if (!passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            return FromResult<AuthResponse>(Result<AuthResponse>.Failure(AppErrors.Unauthorized("Email or password is incorrect.")));
        }

        if (passwordHasher.NeedsRehash(user.PasswordHash))
        {
            user.PasswordHash = passwordHasher.Hash(request.Password);
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

        if (refreshToken is null || refreshToken.ExpiresAtUtc <= now || !IsUsable(refreshToken, now))
        {
            return FromResult<AuthResponse>(Result<AuthResponse>.Failure(AppErrors.Unauthorized("Refresh token is invalid or expired.")));
        }

        var newRawToken = GenerateRawToken();
        var newTokenHash = ComputeTokenHash(newRawToken);

        if (!refreshToken.RevokedAtUtc.HasValue)
        {
            refreshToken.RevokedAtUtc = now;
            refreshToken.ReplacedAtUtc = now;
            refreshToken.ReplacedByTokenHash = newTokenHash;
            refreshToken.UpdatedAtUtc = now;
        }

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
        if (refreshToken is not null)
        {
            refreshToken.RevokedAtUtc ??= clock.UtcNow;
            refreshToken.ReplacedAtUtc = null; // also closes the reuse grace window
            refreshToken.UpdatedAtUtc = clock.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        return NoContent();
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting(AuthRateLimiting.PolicyName)]
    [ProducesResponseType(typeof(ForgotPasswordResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request,
        [FromServices] IValidator<ForgotPasswordRequest> validator,
        [FromServices] AppDbContext db,
        [FromServices] IEmailOutbox emailOutbox,
        [FromServices] IClock clock,
        [FromServices] IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        var validationResponse = await ValidateAsync(request, validator, cancellationToken);
        if (validationResponse is not null)
        {
            return validationResponse;
        }

        var email = request.Email.Trim().ToLowerInvariant();
        var userId = await db.Users.Where(x => x.Email == email).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(cancellationToken);
        if (userId is null)
        {
            // Same response, and the email is sent in the background, so callers can't tell which emails are registered.
            return Ok(new ForgotPasswordResponse(ForgotPasswordMessage, null));
        }

        var now = clock.UtcNow;
        await db.PasswordResetTokens
            .Where(x => x.UserId == userId && x.UsedAtUtc == null && x.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.UsedAtUtc, now).SetProperty(x => x.UpdatedAtUtc, now), cancellationToken);

        var code = PasswordResetCode.Generate();
        var resetToken = new PasswordResetToken
        {
            UserId = userId.Value,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            ExpiresAtUtc = now.Add(PasswordResetCode.Lifetime)
        };
        resetToken.TokenHash = PasswordResetCode.Hash(resetToken.Id, code);
        db.PasswordResetTokens.Add(resetToken);
        await db.SaveChangesAsync(cancellationToken);

        emailOutbox.Enqueue(BuildResetCodeEmail(email, code));
        return Ok(new ForgotPasswordResponse(ForgotPasswordMessage, environment.IsDevelopment() ? code : null));
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting(AuthRateLimiting.PolicyName)]
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

        var now = clock.UtcNow;
        var resetToken = request.Email is null
            ? await FindLegacyResetTokenAsync(db, request.Token, now, cancellationToken)
            : await RedeemResetCodeAsync(db, request.Email, request.Token.Trim(), now, cancellationToken);

        if (resetToken is null)
        {
            var message = request.Email is null ? "Reset token is invalid or expired." : InvalidResetCodeMessage;
            return FromResult(Result.Failure(AppErrors.InvalidRequest(message)));
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Conditional update: of two requests racing with the same code or token, only one can use it.
        var claimed = await db.PasswordResetTokens
            .Where(x => x.Id == resetToken.Id && x.UsedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.UsedAtUtc, now).SetProperty(x => x.UpdatedAtUtc, now), cancellationToken);
        if (claimed == 0)
        {
            return FromResult(Result.Failure(AppErrors.InvalidRequest(
                request.Email is null ? "Reset token is invalid or expired." : InvalidResetCodeMessage)));
        }

        var user = await db.Users.FirstAsync(x => x.Id == resetToken.UserId, cancellationToken);
        user.PasswordHash = passwordHasher.Hash(request.NewPassword);
        user.UpdatedAtUtc = now;
        await RevokeRefreshTokensAsync(db, user.Id, now, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    private static Task<PasswordResetToken?> FindLegacyResetTokenAsync(
        AppDbContext db, string token, DateTime now, CancellationToken cancellationToken)
    {
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        return db.PasswordResetTokens.AsNoTracking().FirstOrDefaultAsync(
            x => x.TokenHash == tokenHash && x.UsedAtUtc == null && x.ExpiresAtUtc > now,
            cancellationToken);
    }

    /// <summary>Checks a code against the user's latest active one. Every check uses up an attempt, wrong or right.</summary>
    private static async Task<PasswordResetToken?> RedeemResetCodeAsync(
        AppDbContext db, string email, string code, DateTime now, CancellationToken cancellationToken)
    {
        if (!PasswordResetCode.IsWellFormed(code))
        {
            return null;
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var resetToken = await db.PasswordResetTokens.AsNoTracking()
            .Where(x => x.User.Email == normalizedEmail && x.UsedAtUtc == null && x.ExpiresAtUtc > now)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (resetToken is null)
        {
            return null;
        }

        // Taken atomically before the check, so parallel guesses can't exceed the attempt limit.
        var attemptTaken = await db.PasswordResetTokens
            .Where(x => x.Id == resetToken.Id && x.UsedAtUtc == null && x.AttemptCount < PasswordResetCode.MaxAttempts)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1), cancellationToken);

        return attemptTaken == 1 && PasswordResetCode.Matches(resetToken.Id, code, resetToken.TokenHash)
            ? resetToken
            : null;
    }

    private static async Task RevokeRefreshTokensAsync(AppDbContext db, Guid userId, DateTime now, CancellationToken cancellationToken)
    {
        var graceStart = now - RefreshReuseGrace;
        var activeRefreshTokens = await db.RefreshTokens
            .Where(x => x.UserId == userId && x.ExpiresAtUtc > now &&
                (x.RevokedAtUtc == null || x.ReplacedAtUtc > graceStart))
            .ToListAsync(cancellationToken);

        foreach (var refreshToken in activeRefreshTokens)
        {
            refreshToken.RevokedAtUtc ??= now;
            refreshToken.ReplacedAtUtc = null;
            refreshToken.UpdatedAtUtc = now;
        }
    }

    private static EmailMessage BuildResetCodeEmail(string email, string code)
    {
        var minutes = (int)PasswordResetCode.Lifetime.TotalMinutes;
        var text = $"Your ANITIME code is {code}. It expires in {minutes} minutes.";
        var html = $"<p>Your ANITIME code is <strong style=\"font-size:20px;letter-spacing:2px\">{code}</strong>.</p>" +
            $"<p>It expires in {minutes} minutes. If you didn't ask to reset your password, you can ignore this email.</p>";
        return new EmailMessage(email, "Your ANITIME password reset code", text + " If you didn't ask to reset your password, you can ignore this email.", html);
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

    private static bool IsUsable(RefreshToken token, DateTime now) =>
        !token.RevokedAtUtc.HasValue || token.ReplacedAtUtc > now - RefreshReuseGrace;

    private static string GenerateRawToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private static string ComputeTokenHash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
