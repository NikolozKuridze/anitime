namespace Anitime.Application.Abstractions;

public sealed record EmailMessage(string To, string Subject, string TextBody, string HtmlBody);

/// <summary>Delivers one email. Implementations must never log the body outside Development: it can hold a reset code.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>Queues email for background delivery, so a request's response time doesn't reveal whether an email was sent.</summary>
public interface IEmailOutbox
{
    void Enqueue(EmailMessage message);
}
