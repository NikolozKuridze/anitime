namespace Anitime.Application.Abstractions;

public interface IUserContext
{
    Guid GetRequiredUserId();
}
