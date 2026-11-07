using System.Text;
using System.Text.Json;
using Anitime.Application.Abstractions;
using Anitime.Contracts;
using Anitime.Infrastructure;
using Anitime.Infrastructure.Options;
using Anitime.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<ReminderDispatchWorker>();
builder.Services.AddHostedService<ReminderNotificationConsumer>();

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    await EnsureDatabaseReadyAsync(scope.ServiceProvider, host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup"), host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
}

host.Run();

static async Task EnsureDatabaseReadyAsync(IServiceProvider services, ILogger logger, CancellationToken cancellationToken)
{
    const int maxAttempts = 40;

    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            var db = services.GetRequiredService<AppDbContext>();
            var pendingMigrations = await db.Database.GetPendingMigrationsAsync(cancellationToken);
            if (!pendingMigrations.Any())
            {
                return;
            }

            throw new InvalidOperationException("Database migrations are not ready yet.");
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException or InvalidOperationException)
        {
            if (attempt == maxAttempts)
            {
                throw;
            }

            logger.LogWarning(ex, "Database is not ready yet. Retry {Attempt}/{MaxAttempts}", attempt, maxAttempts);
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        }
    }
}

public sealed class ReminderDispatchWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReminderDispatchWorker> _logger;

    public ReminderDispatchWorker(IServiceScopeFactory scopeFactory, ILogger<ReminderDispatchWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var clock = scope.ServiceProvider.GetRequiredService<IClock>();
                var publisher = scope.ServiceProvider.GetRequiredService<IReminderPublisher>();

                var now = clock.UtcNow;
                var dueReminders = await db.Reminders
                    .Where(x => x.IsEnabled && x.ScheduledForUtc <= now)
                    .OrderBy(x => x.ScheduledForUtc)
                    .Take(100)
                    .ToListAsync(stoppingToken);

                foreach (var reminder in dueReminders)
                {
                    var message = new ReminderTriggeredIntegrationEvent(
                        reminder.Id,
                        reminder.UserId,
                        reminder.Title,
                        reminder.Message,
                        now,
                        reminder.ScheduledForUtc,
                        reminder.RepeatDaily);

                    await publisher.PublishAsync(message, stoppingToken);

                    reminder.LastTriggeredAtUtc = now;
                    reminder.UpdatedAtUtc = now;
                    if (reminder.RepeatDaily)
                    {
                        reminder.ScheduledForUtc = reminder.ScheduledForUtc.AddDays(1);
                    }
                    else
                    {
                        reminder.IsEnabled = false;
                    }
                }

                if (dueReminders.Count > 0)
                {
                    await db.SaveChangesAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to dispatch reminders.");
            }
        }
    }
}

public sealed class ReminderNotificationConsumer : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<ReminderNotificationConsumer> _logger;
    private IConnection? _connection;
    private IModel? _channel;

    public ReminderNotificationConsumer(
        IServiceScopeFactory scopeFactory,
        IOptions<RabbitMqOptions> options,
        ILogger<ReminderNotificationConsumer> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();
        _channel.QueueDeclare(_options.QueueName, durable: true, exclusive: false, autoDelete: false);
        _channel.BasicQos(0, 10, false);

        return base.StartAsync(cancellationToken);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_channel is null)
        {
            throw new InvalidOperationException("RabbitMQ channel is not initialized.");
        }

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += async (_, args) =>
        {
            try
            {
                var payload = Encoding.UTF8.GetString(args.Body.ToArray());
                var message = JsonSerializer.Deserialize<ReminderTriggeredIntegrationEvent>(payload);
                if (message is null)
                {
                    _channel.BasicAck(args.DeliveryTag, false);
                    return;
                }

                using var scope = _scopeFactory.CreateScope();
                var sender = scope.ServiceProvider.GetRequiredService<IPushNotificationSender>();
                await sender.SendAsync(message, stoppingToken);

                _channel.BasicAck(args.DeliveryTag, false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process reminder event.");
                _channel.BasicNack(args.DeliveryTag, false, true);
            }
        };

        _channel.BasicConsume(queue: _options.QueueName, autoAck: false, consumer: consumer);
        return Task.CompletedTask;
    }

    public override void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        base.Dispose();
    }
}
