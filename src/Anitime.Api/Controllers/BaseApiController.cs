using Anitime.Application.Common;
using Anitime.Application.Validation;
using Anitime.Contracts;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Anitime.Api.Controllers;

[ApiController]
public abstract class BaseApiController : ControllerBase
{
    protected async Task<IActionResult?> ValidateAsync<TRequest>(
        TRequest request,
        IValidator<TRequest> validator,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (validation.IsValid)
        {
            return null;
        }

        return FromResult(Result.Failure(AppErrors.Validation(new Dictionary<string, string[]>(validation.ToDictionary(), StringComparer.Ordinal))));
    }

    protected IActionResult FromResult(Result result)
    {
        if (result.IsSuccess)
        {
            return NoContent();
        }

        return BuildErrorResponse(result.Error!);
    }

    protected IActionResult FromResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return BuildErrorResponse(result.Error!);
    }

    protected IActionResult FromCreatedResult<T>(string location, Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Created(location, result.Value);
        }

        return BuildErrorResponse(result.Error!);
    }

    private ObjectResult BuildErrorResponse(AppError error)
    {
        var response = new ApiErrorResponse(
            error.Code,
            error.Message,
            error.Details,
            HttpContext.TraceIdentifier);

        return StatusCode(error.StatusCode, response);
    }
}
