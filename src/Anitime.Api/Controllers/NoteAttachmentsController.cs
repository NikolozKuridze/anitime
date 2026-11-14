using Anitime.Api.Infrastructure;
using Anitime.Api.Mapping;
using Anitime.Application.Abstractions;
using Anitime.Application.Common;
using Anitime.Contracts;
using Anitime.Domain.Entities;
using Anitime.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Anitime.Api.Controllers;

/// <summary>
/// Images for notes. Upload, put the returned <c>assetId</c> in an image node, and show it through a short-lived
/// signed URL (the editor WebView can't send the bearer token).
/// </summary>
[Authorize]
[Route("api/notes/attachments")]
public sealed class NoteAttachmentsController : BaseApiController
{
    public static readonly TimeSpan SignedUrlLifetime = TimeSpan.FromMinutes(10);
    private const string NotFoundMessage = "Attachment was not found.";

    [HttpPost]
    [RequestSizeLimit(ApiMappings.MaxImageRequestBytes)]
    [RejectOversizedImageRequest]
    [ProducesResponseType(typeof(NoteAttachmentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> Upload(
        IFormFile? file,
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        [FromServices] IFileStorageService storage,
        [FromServices] IClock clock,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return FromResult(Result.Failure(AppErrors.InvalidRequest("Send the image as multipart form field \"file\".")));
        }

        var fileError = ApiMappings.ValidateImageFile(file);
        if (fileError is not null)
        {
            return FromResult(Result.Failure(fileError));
        }

        var userId = userContext.GetRequiredUserId();
        var contentType = ApiMappings.DetectImageContentType(file)!;
        await using var stream = file.OpenReadStream();
        var objectName = await storage.UploadAsync(ApiMappings.BuildObjectName(userId, "notes", contentType), stream, contentType, cancellationToken);

        var now = clock.UtcNow;
        var attachment = new NoteAttachment
        {
            UserId = userId,
            ObjectName = objectName,
            ContentType = contentType,
            SizeBytes = file.Length,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.NoteAttachments.Add(attachment);
        await db.SaveChangesAsync(cancellationToken);

        return FromCreatedResult($"/api/notes/attachments/{attachment.Id}/url", Result<NoteAttachmentResponse>.Success(
            new NoteAttachmentResponse(attachment.Id, contentType, attachment.SizeBytes, now)));
    }

    /// <summary>A URL for the image that works without a token for 10 minutes. Owner only.</summary>
    [HttpGet("{id:guid}/url")]
    [ProducesResponseType(typeof(AttachmentUrlResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SignedUrl(
        Guid id,
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        [FromServices] IAttachmentUrlSigner signer,
        [FromServices] IClock clock,
        CancellationToken cancellationToken)
    {
        var userId = userContext.GetRequiredUserId();
        if (!await db.NoteAttachments.AnyAsync(a => a.Id == id && a.UserId == userId, cancellationToken))
        {
            return FromResult(Result.Failure(AppErrors.NotFound(NotFoundMessage)));
        }

        var expires = new DateTimeOffset(DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc)).Add(SignedUrlLifetime).ToUnixTimeSeconds();
        var signature = signer.Sign(id, expires);
        var url = $"{Request.Scheme}://{Request.Host}/api/notes/attachments/{id}/content?exp={expires}&sig={signature}";
        return Ok(new AttachmentUrlResponse(url, DateTimeOffset.FromUnixTimeSeconds(expires).UtcDateTime));
    }

    /// <summary>The image bytes, for a signed URL from <c>GET {id}/url</c>. 403 when the link is invalid or expired.</summary>
    [AllowAnonymous]
    [HttpGet("{id:guid}/content")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Content(
        Guid id,
        [FromQuery] long exp,
        [FromQuery] string? sig,
        [FromServices] AppDbContext db,
        [FromServices] IFileStorageService storage,
        [FromServices] IAttachmentUrlSigner signer,
        [FromServices] IClock clock,
        CancellationToken cancellationToken)
    {
        var now = new DateTimeOffset(DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc));
        if (!signer.IsValid(id, exp, sig, now))
        {
            return FromResult(Result.Failure(AppErrors.Forbidden("This image link is invalid or has expired. Request a new one.")));
        }

        var attachment = await db.NoteAttachments.AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new { a.ObjectName, a.ContentType })
            .FirstOrDefaultAsync(cancellationToken);
        var stream = attachment is null ? null : await storage.OpenReadAsync(attachment.ObjectName, cancellationToken);
        if (stream is null)
        {
            return FromResult(Result.Failure(AppErrors.NotFound(NotFoundMessage)));
        }

        // Cacheable by this viewer until the link expires; never by shared caches.
        Response.Headers.CacheControl = $"private, max-age={Math.Max(0, exp - now.ToUnixTimeSeconds())}";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(stream, attachment!.ContentType);
    }
}
