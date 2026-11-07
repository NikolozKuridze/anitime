using Anitime.Application.Validation;
using Anitime.Contracts;
using FluentValidation.TestHelper;
using Xunit;

namespace Anitime.Application.Tests;

public sealed class TaskValidatorTests
{
    private readonly CreateTaskRequestValidator _validator = new();

    [Fact]
    public void CreateTask_AllowsEmoji()
    {
        var model = new CreateTaskRequest(
            "Pay bills",
            "Utility payments",
            "💸",
            DateOnly.FromDateTime(DateTime.UtcNow),
            [new ChecklistItemRequest("Electricity")]);

        var result = _validator.TestValidate(model);

        result.ShouldNotHaveValidationErrorFor(x => x.Emoji);
    }

    [Fact]
    public void CreateTask_ShouldRejectEmptyChecklistItemTitle()
    {
        var model = new CreateTaskRequest(
            "Plan trip",
            null,
            "✈️",
            DateOnly.FromDateTime(DateTime.UtcNow),
            [new ChecklistItemRequest("")]);

        var result = _validator.TestValidate(model);

        result.ShouldHaveAnyValidationError();
    }
}
