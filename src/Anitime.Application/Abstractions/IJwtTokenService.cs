using Anitime.Domain.Entities;

namespace Anitime.Application.Abstractions;

public interface IJwtTokenService
{
    (string Token, DateTime ExpiresAtUtc) Create(User user);
}
