using Anitime.Contracts;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Anitime.Api.Swagger;

public sealed class DefaultResponsesOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        AddErrorResponse(operation, "400", "Bad Request", "validation_failed", "One or more validation errors occurred.");
        AddErrorResponse(operation, "401", "Unauthorized", "unauthorized", "Authentication failed.");
        AddErrorResponse(operation, "403", "Forbidden", "forbidden", "Access denied.");
        AddErrorResponse(operation, "404", "Not Found", "not_found", "Resource was not found.");
        AddErrorResponse(operation, "409", "Conflict", "conflict", "Requested resource conflicts with current state.");
    }

    private static void AddErrorResponse(OpenApiOperation operation, string statusCode, string description, string code, string message)
    {
        if (operation.Responses.ContainsKey(statusCode))
        {
            var mediaType = EnsureMediaType(operation, statusCode, description);
            mediaType.Example = BuildErrorExample(code, message);
            return;
        }

        operation.Responses[statusCode] = new OpenApiResponse
        {
            Description = description,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new()
                {
                    Schema = new OpenApiSchema
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.Schema,
                            Id = nameof(ApiErrorResponse)
                        }
                    },
                    Example = BuildErrorExample(code, message)
                }
            }
        };
    }

    private static OpenApiMediaType EnsureMediaType(OpenApiOperation operation, string statusCode, string description)
    {
        if (!operation.Responses.TryGetValue(statusCode, out var response))
        {
            response = new OpenApiResponse { Description = description };
            operation.Responses[statusCode] = response;
        }

        if (!response.Content.TryGetValue("application/json", out var mediaType))
        {
            mediaType = new OpenApiMediaType();
            response.Content["application/json"] = mediaType;
        }

        return mediaType;
    }

    private static OpenApiObject BuildErrorExample(string code, string message) =>
        new()
        {
            ["code"] = new OpenApiString(code),
            ["message"] = new OpenApiString(message),
            ["errors"] = new OpenApiObject(),
            ["traceId"] = new OpenApiString("00-7d7f7f7f7f7f7f7f7d7f7f7f7f7f7f7f-7d7f7f7f7f7f7f7f-00")
        };
}
