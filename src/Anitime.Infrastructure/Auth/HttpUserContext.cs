using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Anitime.Application.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Anitime.Infrastructure.Auth;

public sealed class HttpUserContext : IUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid GetRequiredUserId()
    {
        var raw = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? _httpContextAccessor.HttpContext?.User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        return Guid.TryParse(raw, out var userId)
            ? userId
            : throw new UnauthorizedAccessException("Authenticated user id is missing.");
    }
}
