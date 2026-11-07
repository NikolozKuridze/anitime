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
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqReminderPublisher> _logger;
    private readonly Lazy<IConnection> _connection;

    public RabbitMqReminderPublisher(IOptions<RabbitMqOptions> options, ILogger<RabbitMqReminderPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;
        _connection = new Lazy<IConnection>(CreateConnection);
    }

    public Task PublishAsync(ReminderTriggeredIntegrationEvent message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var channel = _connection.Value.CreateModel();
        channel.QueueDeclare(_options.QueueName, durable: true, exclusive: false, autoDelete: false);

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
        var properties = channel.CreateBasicProperties();
        properties.Persistent = true;

        channel.BasicPublish(exchange: string.Empty, routingKey: _options.QueueName, basicProperties: properties, body: body);
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
