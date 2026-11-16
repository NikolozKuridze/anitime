using Anitime.Contracts;
using Anitime.Domain.Enums;
using FluentValidation;

namespace Anitime.Application.Validation;

public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Gender).IsInEnum();
        RuleFor(x => x.DateOfBirth)
            .Must(x => x is null || x.Value < DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Date of birth must be in the past.");
        RuleFor(x => x.DateOfBirth)
            .Must(x => x is null || x.Value > DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-120))
            .WithMessage("Date of birth is out of range.");
    }
}

public sealed class UpdateUserSettingsRequestValidator : AbstractValidator<UpdateUserSettingsRequest>
{
    public UpdateUserSettingsRequestValidator()
    {
        RuleFor(x => x.BackgroundStyleType).IsInEnum();
        RuleFor(x => x.BackgroundPrimaryColor).MaximumLength(32);
        RuleFor(x => x.BackgroundSecondaryColor).MaximumLength(32);
        RuleFor(x => x.AccentColor).MaximumLength(32);
        RuleFor(x => x.Locale).MaximumLength(16);
        RuleFor(x => x.TimeZone).MaximumLength(64);

    }
}

/// <summary>
/// Solid and Gradient need colours. A request without them (the app switching style before a colour pair is
/// picked) is not refused: the missing colours fall back to the stored ones, then to the default pair.
/// </summary>
public static class UserSettingsRules
{
    public const string DefaultPrimaryColor = "#FDE2EC";
    public const string DefaultSecondaryColor = "#F9D5E5";

    public static (string? Primary, string? Secondary) ResolveColors(
        BackgroundStyleType style, string? requestedPrimary, string? requestedSecondary, string? storedPrimary, string? storedSecondary)
    {
        var primary = Clean(requestedPrimary);
        var secondary = Clean(requestedSecondary);
        if (style is not (BackgroundStyleType.Solid or BackgroundStyleType.Gradient))
        {
            return (primary, secondary);
        }

        primary ??= Clean(storedPrimary) ?? DefaultPrimaryColor;
        if (style == BackgroundStyleType.Gradient)
        {
            secondary ??= Clean(storedSecondary) ?? (primary == DefaultPrimaryColor ? DefaultSecondaryColor : primary);
        }

        return (primary, secondary);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
