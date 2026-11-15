using Anitime.Domain.Enums;
using Anitime.Contracts;
using FluentValidation;

namespace Anitime.Application.Validation;

public sealed class ChecklistItemRequestValidator : AbstractValidator<ChecklistItemRequest>
{
    public ChecklistItemRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(180);
    }
}

public sealed class CreateTaskRequestValidator : AbstractValidator<CreateTaskRequest>
{
    public CreateTaskRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Emoji).MaximumLength(16);
        RuleFor(x => x.ClientRequestId).MaximumLength(64);
        RuleFor(x => x.Priority).Must(TaskPriorities.IsValid!).WithMessage(TaskPriorities.Message).When(x => x.Priority is not null);
        RuleFor(x => x.ChecklistItems).Must(x => x is null || x.Count <= 20)
            .WithMessage("A task can contain at most 20 checklist items.");
        RuleForEach(x => x.ChecklistItems!).SetValidator(new ChecklistItemRequestValidator())
            .When(x => x.ChecklistItems is not null);
    }
}

public sealed class UpdateTaskRequestValidator : AbstractValidator<UpdateTaskRequest>
{
    public UpdateTaskRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Emoji).MaximumLength(16);
        RuleFor(x => x.Priority).Must(TaskPriorities.IsValid!).WithMessage(TaskPriorities.Message).When(x => x.Priority is not null);
    }
}

public static class TaskPriorities
{
    public const string Message = "Priority must be Low, Medium or High.";

    public static bool IsValid(string value) => Parse(value) is not null;

    /// <summary>Names only, any case; numbers are refused so "1" can't silently mean Medium.</summary>
    public static TaskPriority? Parse(string? value) =>
        Enum.GetNames<TaskPriority>().FirstOrDefault(name => string.Equals(name, value?.Trim(), StringComparison.OrdinalIgnoreCase)) is { } name
            ? Enum.Parse<TaskPriority>(name)
            : null;
}
