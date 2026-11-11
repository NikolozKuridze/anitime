namespace Anitime.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";
    public const string ResendProvider = "Resend";

    public string? Provider { get; init; }
    public string? ResendApiKey { get; init; }
    public string? From { get; init; }

    public bool IsResendConfigured =>
        string.Equals(Provider, ResendProvider, StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(ResendApiKey) &&
        !string.IsNullOrWhiteSpace(From);
}
