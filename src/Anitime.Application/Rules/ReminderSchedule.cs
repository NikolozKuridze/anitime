namespace Anitime.Application.Rules;

public static class ReminderSchedule
{
    /// <summary>A reminder delivered later than this is no longer useful, so it is skipped (e.g. a backlog after downtime).</summary>
    public static readonly TimeSpan MaxDeliveryDelay = TimeSpan.FromHours(1);

    public static bool IsStale(DateTime scheduledForUtc, DateTime nowUtc) => nowUtc - scheduledForUtc > MaxDeliveryDelay;

    /// <summary>The first daily occurrence after now, so downtime produces one late reminder rather than a burst.</summary>
    public static DateTime NextDailyOccurrence(DateTime scheduledForUtc, DateTime nowUtc)
    {
        var next = scheduledForUtc.AddDays(1);
        if (next > nowUtc)
        {
            return next;
        }

        var missedDays = (int)Math.Ceiling((nowUtc - next).TotalDays);
        next = next.AddDays(missedDays);
        return next > nowUtc ? next : next.AddDays(1);
    }
}
