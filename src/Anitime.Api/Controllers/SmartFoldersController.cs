using System.Text.Json;
using Anitime.Api.Infrastructure;
using Anitime.Application.Abstractions;
using Anitime.Application.Common;
using Anitime.Application.Rules;
using Anitime.Contracts;
using Anitime.Domain.Entities;
using Anitime.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Anitime.Api.Controllers;

/// <summary>Saved note filters. The server evaluates them: <c>GET /api/notes?smartFolderId={id}</c>.</summary>
[Authorize]
[InvalidRequestBody]
[Route("api/notes/smart-folders")]
public sealed class SmartFoldersController : BaseApiController
{
    private const string NotFoundMessage = "Smart folder was not found.";

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyCollection<SmartFolderResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var userId = userContext.GetRequiredUserId();
        var folders = await db.SmartFolders.AsNoTracking()
            .Where(f => f.UserId == userId)
            .OrderBy(f => f.Name)
            .ToListAsync(cancellationToken);
        return Ok(folders.Select(ToResponse).ToList());
    }

    [HttpPost]
    [ProducesResponseType(typeof(SmartFolderResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] SaveSmartFolderRequest request,
        [FromServices] IValidator<SaveSmartFolderRequest> validator,
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        [FromServices] IClock clock,
        CancellationToken cancellationToken)
    {
        var validationResponse = await ValidateAsync(request, validator, cancellationToken);
        if (validationResponse is not null)
        {
            return validationResponse;
        }

        var now = clock.UtcNow;
        var folder = new SmartFolder
        {
            UserId = userContext.GetRequiredUserId(),
            Name = request.Name.Trim(),
            FilterJson = Serialize(request.Filter),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.SmartFolders.Add(folder);
        await db.SaveChangesAsync(cancellationToken);
        return FromCreatedResult($"/api/notes/smart-folders/{folder.Id}", Result<SmartFolderResponse>.Success(ToResponse(folder)));
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(SmartFolderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] SaveSmartFolderRequest request,
        [FromServices] IValidator<SaveSmartFolderRequest> validator,
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        [FromServices] IClock clock,
        CancellationToken cancellationToken)
    {
        var validationResponse = await ValidateAsync(request, validator, cancellationToken);
        if (validationResponse is not null)
        {
            return validationResponse;
        }

        var userId = userContext.GetRequiredUserId();
        var folder = await db.SmartFolders.FirstOrDefaultAsync(f => f.Id == id && f.UserId == userId, cancellationToken);
        if (folder is null)
        {
            return FromResult(Result.Failure(AppErrors.NotFound(NotFoundMessage)));
        }

        folder.Name = request.Name.Trim();
        folder.FilterJson = Serialize(request.Filter);
        folder.UpdatedAtUtc = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(folder));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        Guid id,
        [FromServices] AppDbContext db,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var userId = userContext.GetRequiredUserId();
        var folder = await db.SmartFolders.FirstOrDefaultAsync(f => f.Id == id && f.UserId == userId, cancellationToken);
        if (folder is null)
        {
            return FromResult(Result.Failure(AppErrors.NotFound(NotFoundMessage)));
        }

        db.SmartFolders.Remove(folder);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // Stored normalized ("#Work" → "work"), so evaluation and the app see the same tags.
    private static string Serialize(SmartFolderFilter filter) =>
        JsonSerializer.Serialize(filter with
        {
            Tags = filter.Tags?.Select(NoteContentIndex.NormalizeTag).OfType<string>().Distinct(StringComparer.Ordinal).ToList()
        });

    private static SmartFolderResponse ToResponse(SmartFolder folder) =>
        new(folder.Id, folder.Name, JsonSerializer.Deserialize<SmartFolderFilter>(folder.FilterJson)!, folder.CreatedAtUtc, folder.UpdatedAtUtc);
}
