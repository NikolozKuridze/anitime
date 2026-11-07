using Anitime.Application.Validation;
using Anitime.Contracts;
using FluentValidation.TestHelper;
using Xunit;

namespace Anitime.Application.Tests;

public sealed class ReminderValidatorTests
{
    private readonly CreateReminderRequestValidator _validator = new();

    [Fact]
    public void CreateReminder_ShouldRejectPastSchedule()
    {
        var model = new CreateReminderRequest("Walk", "Go outside", DateTime.UtcNow.AddHours(-2), false);

        var result = _validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.ScheduledForUtc);
    }
}
