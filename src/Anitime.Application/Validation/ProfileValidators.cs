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

        When(x => x.BackgroundStyleType == BackgroundStyleType.Solid, () =>
        {
            RuleFor(x => x.BackgroundPrimaryColor).NotEmpty();
        });

        When(x => x.BackgroundStyleType == BackgroundStyleType.Gradient, () =>
        {
            RuleFor(x => x.BackgroundPrimaryColor).NotEmpty();
            RuleFor(x => x.BackgroundSecondaryColor).NotEmpty();
        });
    }
}
