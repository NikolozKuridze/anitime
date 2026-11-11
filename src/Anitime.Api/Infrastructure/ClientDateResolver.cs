using Anitime.Application.Abstractions;

namespace Anitime.Api.Infrastructure;

public static class ClientDateResolver
{
    private const string ClientLocalDateHeader = "X-Client-Local-Date";
    private const string TimezoneOffsetMinutesHeader = "X-Timezone-Offset-Minutes";
    private const int MaxTimezoneOffsetMinutes = 14 * 60;

    public static DateOnly ResolveToday(HttpRequest request, IClock clock)
    {
        if (request.Headers.TryGetValue(ClientLocalDateHeader, out var localDateValues) &&
            DateOnly.TryParse(localDateValues.ToString(), out var clientLocalDate))
        {
            return clientLocalDate;
        }

        if (request.Headers.TryGetValue(TimezoneOffsetMinutesHeader, out var offsetValues) &&
            int.TryParse(offsetValues.ToString(), out var offsetMinutes) &&
            Math.Abs(offsetMinutes) <= MaxTimezoneOffsetMinutes)
        {
            var clientNow = clock.UtcNow.AddMinutes(-offsetMinutes);
            return DateOnly.FromDateTime(clientNow);
        }

        return clock.Today;
    }
}
