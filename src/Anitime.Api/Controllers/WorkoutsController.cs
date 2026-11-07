using Anitime.Api.Mapping;
using Anitime.Application.Common;
using Anitime.Contracts;
using Anitime.Infrastructure.Options;
using Anitime.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Anitime.Api.Controllers;

[Authorize]
[Route("api/workouts")]
public sealed class WorkoutsController : BaseApiController
{
    private const int RelatedExerciseLimit = 24;

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyCollection<WorkoutSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromServices] AppDbContext db,
        [FromQuery] string? category,
        CancellationToken cancellationToken)
    {
        var query = db.Workouts.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(category))
        {
            var target = category.Trim();
            query = query.Where(x => x.Category == target);
        }

        var workouts = await query
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Title)
            .ToListAsync(cancellationToken);

        return Ok(workouts.Select(x => x.ToSummaryResponse()).ToList());
    }

    [HttpGet("{slug}")]
    [ProducesResponseType(typeof(WorkoutDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBySlug(
        string slug,
        [FromServices] AppDbContext db,
        [FromServices] IOptions<ExerciseLibraryOptions> options,
        CancellationToken cancellationToken)
    {
        var workout = await db.Workouts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Slug == slug, cancellationToken);

        if (workout is null)
        {
            return FromResult(Result<WorkoutDetailResponse>.Failure(AppErrors.NotFound("Workout was not found.")));
        }

        var imageBaseUrl = options.Value.ImageBaseUrl;
        var muscles = workout.TargetMuscles;

        var relatedExercises = muscles.Count == 0
            ? []
            : await db.Exercises.AsNoTracking()
                .Where(x => x.PrimaryMuscles.Any(m => muscles.Contains(m)))
                .OrderBy(x => x.Name)
                .Take(RelatedExerciseLimit)
                .ToListAsync(cancellationToken);

        var exercises = relatedExercises.Select(x => x.ToSummaryResponse(imageBaseUrl)).ToList();
        return Ok(workout.ToDetailResponse(exercises));
    }
}
