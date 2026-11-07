using Anitime.Contracts;

namespace Anitime.Application.Abstractions;

public interface IReminderPublisher
{
    Task PublishAsync(ReminderTriggeredIntegrationEvent message, CancellationToken cancellationToken);
}
