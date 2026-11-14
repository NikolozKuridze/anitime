using System.Text;
using System.Text.Json;
using Anitime.Application.Abstractions;
using Anitime.Application.Rules;
using Anitime.Contracts;
using Anitime.Infrastructure;
using Anitime.Infrastructure.Notes;
using Anitime.Infrastructure.Notifications;
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
builder.Services.AddHostedService<NotesMaintenanceWorker>();

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
                        reminder.ScheduledForUtc = ReminderSchedule.NextDailyOccurrence(reminder.ScheduledForUtc, now);
                    }
                    else
                    {
                        reminder.IsEnabled = false;
                    }

                    // Saved per reminder so a failure can't re-send the ones already published in this batch.
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

/// <summary>Hourly: deletes notes 30 days in Recently Deleted, and note images unused for 7 days.</summary>
public sealed class NotesMaintenanceWorker(IServiceScopeFactory scopeFactory, ILogger<NotesMaintenanceWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
            using var timer = new PeriodicTimer(Interval);
            do
            {
                await RunOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;
            var purged = await NotesMaintenance.PurgeDeletedNotesAsync(db, now, stoppingToken);
            var images = await NotesMaintenance.CleanUpOrphanAttachmentsAsync(
                db, scope.ServiceProvider.GetRequiredService<IFileStorageService>(), now, stoppingToken);
            if (purged > 0 || images > 0)
            {
                logger.LogInformation("Notes maintenance: purged {Notes} deleted notes and {Images} unused images.", purged, images);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Notes maintenance failed; retrying next hour.");
        }
    }
}

public sealed class ReminderNotificationConsumer : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

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
            VirtualHost = _options.VirtualHost,
            // Required for AsyncEventingBasicConsumer: without it RabbitMQ.Client never invokes the Received handler.
            DispatchConsumersAsync = true
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
                var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;
                if (ReminderSchedule.IsStale(message.ScheduledForUtc, now))
                {
                    _logger.LogInformation("Skipping reminder {ReminderId} scheduled for {ScheduledForUtc}: more than {MaxDelay} late.",
                        message.ReminderId, message.ScheduledForUtc, ReminderSchedule.MaxDeliveryDelay);
                    _channel.BasicAck(args.DeliveryTag, false);
                    return;
                }

                var deduplicator = scope.ServiceProvider.GetRequiredService<ReminderDeliveryDeduplicator>();
                if (!await deduplicator.WasDeliveredAsync(message.ReminderId, message.ScheduledForUtc, stoppingToken))
                {
                    var sender = scope.ServiceProvider.GetRequiredService<IPushNotificationSender>();
                    await sender.SendAsync(message, stoppingToken);
                    await deduplicator.MarkDeliveredAsync(message.ReminderId, message.ScheduledForUtc, stoppingToken);
                }

                _channel.BasicAck(args.DeliveryTag, false);
            }
            catch (JsonException ex)
            {
                // Retrying can never fix a malformed message, and requeueing it would loop forever.
                _logger.LogError(ex, "Dropping malformed reminder event.");
                _channel.BasicNack(args.DeliveryTag, false, false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process reminder event.");
                await Task.Delay(RetryDelay, CancellationToken.None); // keeps an outage from becoming a tight requeue loop
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
