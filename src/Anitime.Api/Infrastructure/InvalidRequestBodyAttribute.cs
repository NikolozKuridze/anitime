using System.Text.RegularExpressions;
using Anitime.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Anitime.Api.Infrastructure;

/// <summary>
/// Turns a body that can't be bound (unknown field, wrong type, malformed JSON) into the API's own
/// <c>400 {code: "invalid_request"}</c> instead of the default ProblemDetails, naming each bad field.
/// Runs just before [ApiController]'s automatic 400 (order -2000).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed partial class InvalidRequestBodyAttribute : Attribute, IActionFilter, IOrderedFilter
{
    private const string UnknownFieldMessage = "Unknown field '{0}'. Update the server contract before sending new fields.";
    private const string InvalidValueMessage = "This value is missing, malformed or of the wrong type.";

    public int Order => -2001;

    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ModelState.IsValid)
        {
            return;
        }

        var errors = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .ToDictionary(
                entry => entry.Key,
                entry => entry.Value!.Errors.Select(error => Describe(error.ErrorMessage)).Distinct().ToArray(),
                StringComparer.Ordinal);

        context.Result = new BadRequestObjectResult(new ApiErrorResponse(
            "invalid_request",
            "The request body has an unknown or invalid field.",
            errors,
            context.HttpContext.TraceIdentifier));
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }

    // System.Text.Json names the .NET type in its message; only the field name is passed on.
    private static string Describe(string message) =>
        UnmappedMember().Match(message) is { Success: true } match
            ? string.Format(UnknownFieldMessage, match.Groups[1].Value)
            : InvalidValueMessage;

    [GeneratedRegex("property '([^']*)' could not be mapped", RegexOptions.CultureInvariant)]
    private static partial Regex UnmappedMember();
}
