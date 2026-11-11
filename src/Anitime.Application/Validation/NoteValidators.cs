using System.Text.RegularExpressions;
using Anitime.Contracts;
using FluentValidation;

namespace Anitime.Application.Validation;

public sealed class RichNoteDocumentValidator : AbstractValidator<RichNoteDocument>
{
    public RichNoteDocumentValidator()
    {
        RuleFor(x => x.Version).Equal(1);
        RuleFor(x => x.Blocks).NotNull();
        RuleFor(x => x.Blocks.Count).LessThanOrEqualTo(NoteValidationRules.MaxBlocks).When(x => x.Blocks is not null);
    }
}

public sealed class CreateNoteRequestValidator : AbstractValidator<CreateNoteRequest>
{
    public CreateNoteRequestValidator()
    {
        RuleFor(x => x).Must(HasMeaningfulContent)
            .WithMessage(NoteValidationRules.EmptyNoteMessage);
        RuleFor(x => x.Title).MaximumLength(160);
        RuleFor(x => x.ContentJson).NotNull().SetValidator(new RichNoteDocumentValidator());
        RuleFor(x => x.FontFamily).MaximumLength(64);
        RuleFor(x => x.FontSize).InclusiveBetween(10, 48);
        RuleFor(x => x.FontWeight).InclusiveBetween(100, 900);
        RuleFor(x => x.TextColor).Must(NoteValidationRules.BeValidColor).WithMessage("TextColor must be a valid HEX color.")
            .When(x => !string.IsNullOrWhiteSpace(x.TextColor));
        RuleFor(x => x.BackgroundColor).Must(NoteValidationRules.BeValidColor).WithMessage("BackgroundColor must be a valid HEX color or transparent.")
            .When(x => !string.IsNullOrWhiteSpace(x.BackgroundColor));
    }

    private static bool HasMeaningfulContent(CreateNoteRequest request) =>
        !string.IsNullOrWhiteSpace(request.Title) ||
        !string.IsNullOrWhiteSpace(request.PlainTextPreview) ||
        (request.ContentJson?.Blocks?.Count ?? 0) > 0;
}

public sealed class UpdateNoteRequestValidator : AbstractValidator<UpdateNoteRequest>
{
    public UpdateNoteRequestValidator()
    {
        // A metadata-only update (no content, no preview) is checked against the stored note by the controller.
        RuleFor(x => x).Must(HasMeaningfulContent)
            .WithMessage(NoteValidationRules.EmptyNoteMessage)
            .When(x => x.ContentJson is not null);
        RuleFor(x => x.Title).MaximumLength(160);
        RuleFor(x => x.ContentJson).NotNull()
            .WithMessage("ContentJson is required when PlainTextPreview is provided.")
            .When(x => x.PlainTextPreview is not null);
        RuleFor(x => x.ContentJson!).SetValidator(new RichNoteDocumentValidator())
            .When(x => x.ContentJson is not null);
        RuleFor(x => x.FontFamily).MaximumLength(64);
        RuleFor(x => x.FontSize).InclusiveBetween(10, 48);
        RuleFor(x => x.FontWeight).InclusiveBetween(100, 900);
        RuleFor(x => x.VersionToken).NotEmpty().MaximumLength(64);
        RuleFor(x => x.TextColor).Must(NoteValidationRules.BeValidColor).WithMessage("TextColor must be a valid HEX color.")
            .When(x => !string.IsNullOrWhiteSpace(x.TextColor));
        RuleFor(x => x.BackgroundColor).Must(NoteValidationRules.BeValidColor).WithMessage("BackgroundColor must be a valid HEX color or transparent.")
            .When(x => !string.IsNullOrWhiteSpace(x.BackgroundColor));
    }

    private static bool HasMeaningfulContent(UpdateNoteRequest request) =>
        !string.IsNullOrWhiteSpace(request.Title) ||
        !string.IsNullOrWhiteSpace(request.PlainTextPreview) ||
        (request.ContentJson?.Blocks?.Count ?? 0) > 0;
}

public sealed class UpdateNoteContentRequestValidator : AbstractValidator<UpdateNoteContentRequest>
{
    public UpdateNoteContentRequestValidator()
    {
        RuleFor(x => x.ContentJson).NotNull().SetValidator(new RichNoteDocumentValidator());
        RuleFor(x => x.VersionToken).NotEmpty().MaximumLength(64);
    }
}

public static class NoteValidationRules
{
    public const int MaxBlocks = 5000;
    public const string EmptyNoteMessage = "A note must contain a title or rich text content.";

    public static bool BeValidColor(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        Regex.IsMatch(value, "^(transparent|#([0-9a-fA-F]{6}|[0-9a-fA-F]{8}))$", RegexOptions.CultureInvariant);
}
