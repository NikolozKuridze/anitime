using Anitime.Application.Abstractions;
using Anitime.Contracts;
using Microsoft.Extensions.Logging;

namespace Anitime.Infrastructure.Notifications;

public sealed class LoggingPushNotificationSender : IPushNotificationSender
{
    private readonly ILogger<LoggingPushNotificationSender> _logger;

    public LoggingPushNotificationSender(ILogger<LoggingPushNotificationSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(ReminderTriggeredIntegrationEvent message, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Push placeholder for user {UserId}: {Title} - {Message}",
            message.UserId,
            message.Title,
            message.Message);

        return Task.CompletedTask;
    }
}
