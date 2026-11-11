using System.Text;
using System.Text.Json;
using Anitime.Application.Abstractions;
using Anitime.Contracts;
using Anitime.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Anitime.Infrastructure.Messaging;

public sealed class RabbitMqReminderPublisher : IReminderPublisher, IDisposable
{
    private static readonly TimeSpan ConfirmTimeout = TimeSpan.FromSeconds(5);

    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqReminderPublisher> _logger;
    private readonly Lazy<IConnection> _connection;

    public RabbitMqReminderPublisher(IOptions<RabbitMqOptions> options, ILogger<RabbitMqReminderPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;
        // PublicationOnly: the default mode caches a failed connection forever, so RabbitMQ being down once would need a restart.
        _connection = new Lazy<IConnection>(CreateConnection, LazyThreadSafetyMode.PublicationOnly);
    }

    public Task PublishAsync(ReminderTriggeredIntegrationEvent message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var channel = _connection.Value.CreateModel();
        channel.QueueDeclare(_options.QueueName, durable: true, exclusive: false, autoDelete: false);
        channel.ConfirmSelect();

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
        var properties = channel.CreateBasicProperties();
        properties.Persistent = true;

        channel.BasicPublish(exchange: string.Empty, routingKey: _options.QueueName, basicProperties: properties, body: body);
        channel.WaitForConfirmsOrDie(ConfirmTimeout); // the caller only marks the reminder as sent once the broker has it
        _logger.LogInformation("Published reminder event {ReminderId}", message.ReminderId);

        return Task.CompletedTask;
    }

    private IConnection CreateConnection()
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost
        };

        return factory.CreateConnection();
    }

    public void Dispose()
    {
        if (_connection.IsValueCreated)
        {
            _connection.Value.Dispose();
        }
    }
}
