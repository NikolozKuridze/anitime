using System.Threading.RateLimiting;
using Anitime.Contracts;
using Microsoft.AspNetCore.RateLimiting;

namespace Anitime.Api.Infrastructure;

public static class AuthRateLimiting
{
    public const string PolicyName = "auth";
    private const string PermitLimitKey = "RateLimiting:AuthPermitLimit";
    private const int DefaultPermitLimit = 20;
    private const string CloudflareClientIpHeader = "CF-Connecting-IP";

    public static IServiceCollection AddAuthRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.OnRejected = (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                return new ValueTask(context.HttpContext.Response.WriteAsJsonAsync(
                    new ApiErrorResponse(
                        "rate_limited",
                        "Too many attempts. Try again in a minute.",
                        null,
                        context.HttpContext.TraceIdentifier),
                    cancellationToken));
            };

            options.AddPolicy(PolicyName, context =>
            {
                var permitLimit = context.RequestServices.GetRequiredService<IConfiguration>()
                    .GetValue(PermitLimitKey, DefaultPermitLimit);

                return RateLimitPartition.GetFixedWindowLimiter(
                    ResolveClientIp(context),
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = TimeSpan.FromMinutes(1) });
            });
        });

    // Behind Cloudflare the connection IP is the proxy, which would make every user share one limit.
    private static string ResolveClientIp(HttpContext context) =>
        context.Request.Headers.TryGetValue(CloudflareClientIpHeader, out var clientIp) && !string.IsNullOrWhiteSpace(clientIp)
            ? clientIp.ToString()
            : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
