using Anitime.Contracts;
using FluentValidation;

namespace Anitime.Application.Validation;

public sealed class CreateReminderRequestValidator : AbstractValidator<CreateReminderRequest>
{
    public CreateReminderRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Message).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.ScheduledForUtc)
            .Must(x => x > DateTime.UtcNow.AddMinutes(-1))
            .WithMessage("Reminder schedule must be in the future.");
    }
}
