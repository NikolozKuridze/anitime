using System.Threading.Channels;
using Anitime.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Anitime.Infrastructure.Email;

/// <summary>An in-memory queue drained by a hosted service. Mail still queued at shutdown is lost; the user can request a new code.</summary>
public sealed class BackgroundEmailOutbox(IServiceScopeFactory scopeFactory, ILogger<BackgroundEmailOutbox> logger)
    : BackgroundService, IEmailOutbox
{
    private const int Capacity = 1000;

    private readonly Channel<EmailMessage> _queue = Channel.CreateBounded<EmailMessage>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    public void Enqueue(EmailMessage message)
    {
        if (!_queue.Writer.TryWrite(message))
        {
            logger.LogWarning("Email queue is full; an email was dropped.");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IEmailSender>().SendAsync(message, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to send an email.");
            }
        }
    }
}
