using Anitime.Contracts;
using FluentValidation;

namespace Anitime.Application.Validation;

public sealed class CreateReminderRequestValidator : AbstractValidator<CreateReminderRequest>
{
    public CreateReminderRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Message).MaximumLength(1000); // optional in the app: missing, null and "" are stored as ""
        RuleFor(x => x.ClientRequestId).MaximumLength(64);
        RuleFor(x => x.ScheduledForUtc)
            .Must(x => x > DateTime.UtcNow.AddMinutes(-1))
            .WithMessage("Reminder schedule must be in the future.");
    }
}

/// <summary>The same rules as <see cref="CreateReminderRequestValidator"/>.</summary>
public sealed class UpdateReminderRequestValidator : AbstractValidator<UpdateReminderRequest>
{
    public UpdateReminderRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Message).MaximumLength(1000); // optional in the app: missing, null and "" are stored as ""
        RuleFor(x => x.ScheduledForUtc)
            .Must(x => x > DateTime.UtcNow.AddMinutes(-1))
            .WithMessage("Reminder schedule must be in the future.");
    }
}
