using System.Text.Json;
using System.Text.Json.Serialization;
using Anitime.Api.Mapping;
using Anitime.Application.Abstractions;
using Anitime.Application.Caching;
using Anitime.Application.Common;
using Anitime.Contracts;
using Anitime.Domain.Entities;
using Anitime.Domain.Enums;
using Anitime.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace Anitime.Api.Controllers;

[Authorize]
[Route("api/profile")]
public sealed class ProfileController : BaseApiController
{
    // Match the MVC JSON output so a cache HIT returns the same shape as a cache MISS:
    // camelCase property names + enums as strings (not integer enum indexes).
    private static readonly JsonSerializerOptions CacheJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    [HttpGet]
    [ProducesResponseType(typeof(UserProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProfile(
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        [FromServices] IDistributedCache cache,
        CancellationToken cancellationToken)
    {
        var userId = userContext.GetRequiredUserId();
        var cacheKey = CacheKeys.Profile(userId);
        var cached = await cache.GetStringAsync(cacheKey, cancellationToken);
        if (!string.IsNullOrWhiteSpace(cached))
        {
            return Content(cached, "application/json");
        }

        var user = await db.Users
            .AsNoTracking()
            .Include(x => x.Settings)
            .FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);

        if (user is null)
        {
            return FromResult(Result<UserProfileResponse>.Failure(AppErrors.NotFound("User profile was not found.")));
        }

        var response = user.ToResponse(Request);
        await cache.SetStringAsync(
            cacheKey,
            JsonSerializer.Serialize(response, CacheJsonOptions),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) },
            cancellationToken);

        return FromResult(Result<UserProfileResponse>.Success(response));
    }

    [HttpGet("profile-image")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProfileImage(
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        [FromServices] IFileStorageService fileStorageService,
        CancellationToken cancellationToken)
    {
        var userId = userContext.GetRequiredUserId();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);

        if (user is null || string.IsNullOrWhiteSpace(user.ProfileImageObjectName))
        {
            return FromResult(Result.Failure(AppErrors.NotFound("Profile image was not found.")));
        }

        var stream = await fileStorageService.OpenReadAsync(user.ProfileImageObjectName, cancellationToken);
        if (stream is null)
        {
            return FromResult(Result.Failure(AppErrors.NotFound("Profile image was not found.")));
        }

        return File(stream, user.ProfileImageContentType ?? "application/octet-stream");
    }

    [HttpGet("background-image")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBackgroundImage(
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        [FromServices] IFileStorageService fileStorageService,
        CancellationToken cancellationToken)
    {
        var userId = userContext.GetRequiredUserId();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);

        if (user is null || string.IsNullOrWhiteSpace(user.BackgroundImageObjectName))
        {
            return FromResult(Result.Failure(AppErrors.NotFound("Background image was not found.")));
        }

        var stream = await fileStorageService.OpenReadAsync(user.BackgroundImageObjectName, cancellationToken);
        if (stream is null)
        {
            return FromResult(Result.Failure(AppErrors.NotFound("Background image was not found.")));
        }

        return File(stream, user.BackgroundImageContentType ?? "application/octet-stream");
    }

    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateProfile(
        [FromBody] UpdateProfileRequest request,
        [FromServices] IValidator<UpdateProfileRequest> validator,
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        [FromServices] IDistributedCache cache,
        [FromServices] IClock clock,
        CancellationToken cancellationToken)
    {
        var validationResponse = await ValidateAsync(request, validator, cancellationToken);
        if (validationResponse is not null)
        {
            return validationResponse;
        }

        var userId = userContext.GetRequiredUserId();
        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null)
        {
            return FromResult(Result.Failure(AppErrors.NotFound("User profile was not found.")));
        }

        user.Name = request.Name.Trim();
        user.Gender = request.Gender;
        user.DateOfBirth = request.DateOfBirth;
        user.UpdatedAtUtc = clock.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync(CacheKeys.Profile(userId), cancellationToken);
        return NoContent();
    }

    [HttpPut("settings")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateSettings(
        [FromBody] UpdateUserSettingsRequest request,
        [FromServices] IValidator<UpdateUserSettingsRequest> validator,
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        [FromServices] IDistributedCache cache,
        [FromServices] IClock clock,
        CancellationToken cancellationToken)
    {
        var validationResponse = await ValidateAsync(request, validator, cancellationToken);
        if (validationResponse is not null)
        {
            return validationResponse;
        }

        var userId = userContext.GetRequiredUserId();
        var settings = await GetOrCreateUserSettingsAsync(db, userId, clock.UtcNow, cancellationToken);
        settings.BackgroundStyleType = request.BackgroundStyleType;
        settings.BackgroundPrimaryColor = request.BackgroundPrimaryColor?.Trim();
        settings.BackgroundSecondaryColor = request.BackgroundSecondaryColor?.Trim();
        settings.AccentColor = request.AccentColor?.Trim();
        settings.Locale = request.Locale?.Trim();
        settings.TimeZone = request.TimeZone?.Trim();
        settings.UpdatedAtUtc = clock.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync(CacheKeys.Profile(userId), cancellationToken);
        return NoContent();
    }

    [HttpPost("profile-image")]
    [DisableRequestSizeLimit]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UploadProfileImage(
        IFormFile file,
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        [FromServices] IFileStorageService fileStorageService,
        [FromServices] IDistributedCache cache,
        [FromServices] IClock clock,
        CancellationToken cancellationToken)
    {
        var userId = userContext.GetRequiredUserId();
        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null)
        {
            return FromResult(Result.Failure(AppErrors.NotFound("User profile was not found.")));
        }

        var fileValidation = ApiMappings.ValidateImageFile(file);
        if (fileValidation is not null)
        {
            return FromResult(Result.Failure(AppErrors.InvalidRequest(fileValidation)));
        }

        await using var stream = file.OpenReadStream();
        var previousObjectName = user.ProfileImageObjectName;
        var objectName = await fileStorageService.UploadAsync(
            ApiMappings.BuildObjectName(userId, "profile", file.FileName),
            stream,
            file.ContentType,
            cancellationToken);

        user.ProfileImageObjectName = objectName;
        user.ProfileImageContentType = file.ContentType;
        user.UpdatedAtUtc = clock.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(previousObjectName) && previousObjectName != objectName)
        {
            await fileStorageService.DeleteAsync(previousObjectName, cancellationToken);
        }

        await cache.RemoveAsync(CacheKeys.Profile(userId), cancellationToken);
        return NoContent();
    }

    [HttpPost("background-image")]
    [DisableRequestSizeLimit]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UploadBackgroundImage(
        IFormFile file,
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        [FromServices] IFileStorageService fileStorageService,
        [FromServices] IDistributedCache cache,
        [FromServices] IClock clock,
        CancellationToken cancellationToken)
    {
        var userId = userContext.GetRequiredUserId();
        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null)
        {
            return FromResult(Result.Failure(AppErrors.NotFound("User profile was not found.")));
        }

        var fileValidation = ApiMappings.ValidateImageFile(file);
        if (fileValidation is not null)
        {
            return FromResult(Result.Failure(AppErrors.InvalidRequest(fileValidation)));
        }

        var settings = await GetOrCreateUserSettingsAsync(db, userId, clock.UtcNow, cancellationToken);

        await using var stream = file.OpenReadStream();
        var previousObjectName = user.BackgroundImageObjectName;
        var objectName = await fileStorageService.UploadAsync(
            ApiMappings.BuildObjectName(userId, "background", file.FileName),
            stream,
            file.ContentType,
            cancellationToken);

        user.BackgroundImageObjectName = objectName;
        user.BackgroundImageContentType = file.ContentType;
        user.UpdatedAtUtc = clock.UtcNow;
        settings.BackgroundStyleType = BackgroundStyleType.Image;
        settings.UpdatedAtUtc = clock.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(previousObjectName) && previousObjectName != objectName)
        {
            await fileStorageService.DeleteAsync(previousObjectName, cancellationToken);
        }

        await cache.RemoveAsync(CacheKeys.Profile(userId), cancellationToken);
        return NoContent();
    }

    private static async Task<UserSettings> GetOrCreateUserSettingsAsync(
        AppDbContext db,
        Guid userId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var settings = await db.UserSettings.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (settings is not null)
        {
            return settings;
        }

        settings = new UserSettings
        {
            UserId = userId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.UserSettings.Add(settings);
        return settings;
    }
}
