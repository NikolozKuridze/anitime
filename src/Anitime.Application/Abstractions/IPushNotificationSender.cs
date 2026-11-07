using Anitime.Contracts;

namespace Anitime.Application.Abstractions;

public interface IPushNotificationSender
{
    Task SendAsync(ReminderTriggeredIntegrationEvent message, CancellationToken cancellationToken);
}
