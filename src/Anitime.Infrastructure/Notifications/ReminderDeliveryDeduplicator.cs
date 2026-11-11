using Microsoft.Extensions.Caching.Distributed;

namespace Anitime.Infrastructure.Notifications;

/// <summary>
/// Publishing is at-least-once: a crash between publish and save re-sends the event.
/// One occurrence of a reminder is identified by its id plus its scheduled time.
/// </summary>
public sealed class ReminderDeliveryDeduplicator(IDistributedCache cache)
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(2);

    public async Task<bool> WasDeliveredAsync(Guid reminderId, DateTime scheduledForUtc, CancellationToken cancellationToken) =>
        await cache.GetStringAsync(Key(reminderId, scheduledForUtc), cancellationToken) is not null;

    public Task MarkDeliveredAsync(Guid reminderId, DateTime scheduledForUtc, CancellationToken cancellationToken) =>
        cache.SetStringAsync(
            Key(reminderId, scheduledForUtc),
            "1",
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Retention },
            cancellationToken);

    private static string Key(Guid reminderId, DateTime scheduledForUtc) =>
        $"reminder-delivered:{reminderId}:{scheduledForUtc.ToUniversalTime():O}";
}
