using System.Security.Cryptography;
using Microsoft.Net.Http.Headers;

namespace Anitime.Api.Infrastructure;

/// <summary>Marks a GET endpoint whose JSON gets an ETag, so an unchanged list is answered with an empty 304.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ConditionalGetAttribute : Attribute;

/// <summary>
/// Hashes the response body into a strong ETag. The tag depends only on the bytes, so it can never serve one
/// user's data to another; <c>private, no-cache</c> makes clients revalidate on every request instead of guessing freshness.
/// </summary>
public sealed class ConditionalGetMiddleware(RequestDelegate next)
{
    private const string CacheControlValue = "private, no-cache";

    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method) ||
            context.GetEndpoint()?.Metadata.GetMetadata<ConditionalGetAttribute>() is null)
        {
            await next(context);
            return;
        }

        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        if (context.Response.StatusCode == StatusCodes.Status200OK)
        {
            var etag = $"\"{Convert.ToHexString(SHA256.HashData(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)))[..32]}\"";
            context.Response.Headers.ETag = etag;
            context.Response.Headers.CacheControl = CacheControlValue;

            if (Matches(context.Request.Headers.IfNoneMatch, etag))
            {
                context.Response.StatusCode = StatusCodes.Status304NotModified;
                context.Response.ContentLength = null;
                context.Response.Headers.ContentType = default;
                return;
            }
        }

        context.Response.ContentLength = buffer.Length;
        buffer.Position = 0;
        await buffer.CopyToAsync(originalBody, context.RequestAborted);
    }

    private static bool Matches(Microsoft.Extensions.Primitives.StringValues ifNoneMatch, string etag) =>
        EntityTagHeaderValue.TryParseList(ifNoneMatch, out var tags) &&
        tags.Any(tag => tag.Equals(EntityTagHeaderValue.Any) || tag.Compare(new EntityTagHeaderValue(etag), useStrongComparison: false));
}
