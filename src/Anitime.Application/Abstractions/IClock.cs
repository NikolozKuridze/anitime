namespace Anitime.Application.Abstractions;

public interface IClock
{
    DateTime UtcNow { get; }
    DateOnly Today { get; }
}
