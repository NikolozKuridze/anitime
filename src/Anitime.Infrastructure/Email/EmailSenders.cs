using System.Net.Http.Headers;
using System.Net.Http.Json;
using Anitime.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anitime.Infrastructure.Email;

/// <summary>Sends through the Resend HTTP API (POST /emails with a Bearer key).</summary>
public sealed class ResendEmailSender(HttpClient httpClient, IOptions<EmailOptions> options) : IEmailSender
{
    public const string BaseAddress = "https://api.resend.com/";

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "emails")
        {
            Content = JsonContent.Create(new
            {
                from = options.Value.From,
                to = new[] { message.To },
                subject = message.Subject,
                text = message.TextBody,
                html = message.HtmlBody
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.ResendApiKey);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // Status only: the response is not needed to diagnose and the request held a reset code.
            throw new HttpRequestException($"Resend rejected the email with status {(int)response.StatusCode}.", null, response.StatusCode);
        }
    }
}

/// <summary>Development only: writes the email, including any code, to the log instead of sending it.</summary>
public sealed class DevelopmentLogEmailSender(ILogger<DevelopmentLogEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        logger.LogInformation("Development email to {To}: {Subject}\n{Body}", message.To, message.Subject, message.TextBody);
        return Task.CompletedTask;
    }
}

/// <summary>Used outside Development when no provider is configured: nothing is sent and nothing secret is logged.</summary>
public sealed class UnconfiguredEmailSender(ILogger<UnconfiguredEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        logger.LogWarning("Email delivery is not configured (set Email:Provider, Email:ResendApiKey and Email:From); an email was not sent.");
        return Task.CompletedTask;
    }
}
