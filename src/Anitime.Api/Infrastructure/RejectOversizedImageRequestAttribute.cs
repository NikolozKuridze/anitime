using Anitime.Api.Mapping;
using Anitime.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Anitime.Api.Infrastructure;

/// <summary>Answers a declared-too-large upload with 413 JSON before the body is read, instead of a dropped connection.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RejectOversizedImageRequestAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        if (context.HttpContext.Request.ContentLength > ApiMappings.MaxImageRequestBytes)
        {
            context.Result = new ObjectResult(new ApiErrorResponse(
                "payload_too_large", ApiMappings.ImageTooLargeMessage, null, context.HttpContext.TraceIdentifier))
            {
                StatusCode = StatusCodes.Status413PayloadTooLarge
            };
        }
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
