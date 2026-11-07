using Anitime.Contracts;
using FluentValidation;

namespace Anitime.Application.Validation;

public sealed class CreateHabitRequestValidator : AbstractValidator<CreateHabitRequest>
{
    public CreateHabitRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Emoji).MaximumLength(16);
        RuleFor(x => x.DurationDays).InclusiveBetween(1, 365);
        RuleFor(x => x.StartDate)
            .Must(x => x is null || x.Value >= DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1))
            .WithMessage("Start date cannot be older than 1 year.");
        RuleFor(x => x.StartDate)
            .Must(x => x is null || x.Value <= DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1))
            .WithMessage("Start date cannot be more than 1 year in the future.");
    }
}

public sealed class ToggleHabitEntryRequestValidator : AbstractValidator<ToggleHabitEntryRequest>
{
    public ToggleHabitEntryRequestValidator()
    {
        RuleFor(x => x.Date)
            .Must(x => x is null || x.Value >= DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1))
            .WithMessage("Completion date is too far in the past.");
        RuleFor(x => x.Date)
            .Must(x => x is null || x.Value <= DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1))
            .WithMessage("Completion date is too far in the future.");
    }
}
