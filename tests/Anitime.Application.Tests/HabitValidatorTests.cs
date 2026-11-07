using Anitime.Application.Validation;
using Anitime.Contracts;
using FluentValidation.TestHelper;
using Xunit;

namespace Anitime.Application.Tests;

public sealed class HabitValidatorTests
{
    private readonly CreateHabitRequestValidator _validator = new();

    [Fact]
    public void CreateHabit_ShouldRequireTitle()
    {
        var model = new CreateHabitRequest("", "🔥", DateOnly.FromDateTime(DateTime.UtcNow), 30);

        var result = _validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public void CreateHabit_ShouldRejectInvalidDuration()
    {
        var model = new CreateHabitRequest("Meditate", "🧘", DateOnly.FromDateTime(DateTime.UtcNow), 0);

        var result = _validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.DurationDays);
    }
}
