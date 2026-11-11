using System.Text.Json;
using Anitime.Api.Infrastructure;
using Anitime.Api.Mapping;
using Anitime.Application.Caching;
using Anitime.Application.Common;
using Anitime.Contracts;
using Anitime.Domain.Entities;
using Anitime.Infrastructure.Options;
using Anitime.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace Anitime.Api.Controllers;

[Authorize]
[Route("api/exercises")]
public sealed class ExercisesController : BaseApiController
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    [HttpGet]
    [ProducesResponseType(typeof(ExerciseListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromServices] AppDbContext db,
        [FromServices] IOptions<ExerciseLibraryOptions> options,
        [FromQuery] string? search,
        [FromQuery] string? muscle,
        [FromQuery] string? equipment,
        [FromQuery] string? category,
        [FromQuery] string? level,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken cancellationToken)
    {
        pageSize = pageSize < 1 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);
        page = Math.Clamp(page, 1, int.MaxValue / MaxPageSize); // keeps the SQL offset from overflowing

        var query = db.Exercises.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => EF.Functions.ILike(x.Name, $"%{term}%"));
        }

        if (!string.IsNullOrWhiteSpace(muscle))
        {
            var target = muscle.Trim().ToLowerInvariant();
            query = query.Where(x => x.PrimaryMuscles.Contains(target) || x.SecondaryMuscles.Contains(target));
        }

        if (!string.IsNullOrWhiteSpace(equipment))
        {
            var target = equipment.Trim();
            query = query.Where(x => x.Equipment == target);
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var target = category.Trim();
            query = query.Where(x => x.Category == target);
        }

        if (!string.IsNullOrWhiteSpace(level))
        {
            var target = level.Trim();
            query = query.Where(x => x.Level == target);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var imageBaseUrl = options.Value.ImageBaseUrl;
        var response = new ExerciseListResponse(
            items.Select(x => x.ToSummaryResponse(imageBaseUrl)).ToList(),
            page,
            pageSize,
            totalCount,
            (int)Math.Ceiling(totalCount / (double)pageSize));

        return Ok(response);
    }

    [HttpGet("filters")]
    [ProducesResponseType(typeof(ExerciseFiltersResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Filters(
        [FromServices] AppDbContext db,
        [FromServices] IDistributedCache cache,
        CancellationToken cancellationToken)
    {
        var cached = await cache.GetStringAsync(CacheKeys.ExerciseFilters, cancellationToken);
        if (!string.IsNullOrWhiteSpace(cached))
        {
            return Content(cached, "application/json");
        }

        var equipment = await db.Exercises.AsNoTracking()
            .Where(x => x.Equipment != null)
            .Select(x => x.Equipment!)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);

        var categories = await db.Exercises.AsNoTracking()
            .Select(x => x.Category)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);

        var levels = await db.Exercises.AsNoTracking()
            .Select(x => x.Level)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);

        // Muscle arrays unnested in the database, then distinct-sorted.
        var muscles = await db.Exercises.AsNoTracking()
            .SelectMany(x => x.PrimaryMuscles.Concat(x.SecondaryMuscles))
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);

        var response = new ExerciseFiltersResponse(muscles, equipment, categories, levels);
        await cache.SetStringAsync(
            CacheKeys.ExerciseFilters,
            JsonSerializer.Serialize(response, CacheJson.Options),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12) },
            cancellationToken);

        return Ok(response);
    }

    [HttpGet("{slug}")]
    [ProducesResponseType(typeof(ExerciseDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBySlug(
        string slug,
        [FromServices] AppDbContext db,
        [FromServices] IOptions<ExerciseLibraryOptions> options,
        CancellationToken cancellationToken)
    {
        var exercise = await db.Exercises.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Slug == slug, cancellationToken);

        if (exercise is null)
        {
            return FromResult(Result<ExerciseDetailResponse>.Failure(AppErrors.NotFound("Exercise was not found.")));
        }

        return Ok(exercise.ToDetailResponse(options.Value.ImageBaseUrl));
    }
}
