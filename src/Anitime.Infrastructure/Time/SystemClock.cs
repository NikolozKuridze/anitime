using Anitime.Application.Abstractions;

namespace Anitime.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
    public DateOnly Today => DateOnly.FromDateTime(UtcNow);
}
