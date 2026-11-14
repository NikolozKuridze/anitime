using System.Text.RegularExpressions;
using Anitime.Application.Rules;
using Anitime.Contracts;
using FluentValidation;

namespace Anitime.Application.Validation;

public sealed class RichNoteDocumentValidator : AbstractValidator<RichNoteDocument>
{
    public RichNoteDocumentValidator()
    {
        RuleFor(x => x.Version).InclusiveBetween(1, RichNoteV2Schema.Version)
            .WithMessage("Version must be 1 or 2.");

        When(x => x.Version == 1, () =>
        {
            RuleFor(x => x.Blocks).NotNull();
            RuleFor(x => x.Blocks!.Count).LessThanOrEqualTo(NoteValidationRules.MaxBlocks).When(x => x.Blocks is not null);
            RuleFor(x => x.Content).Null().WithMessage("Content is only used by version 2 documents.");
        });

        When(x => x.Version == RichNoteV2Schema.Version, () =>
        {
            RuleFor(x => x.Content).NotNull();
            RuleFor(x => x.Blocks).Null().WithMessage("Blocks is only used by version 1 documents.");
            RuleFor(x => x.Content!).Custom((content, context) =>
            {
                foreach (var error in RichNoteV2Schema.Validate(content))
                {
                    context.AddFailure($"ContentJson.{error.Path}", error.Message);
                }
            }).When(x => x.Content is not null);
        });
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
        NoteValidationRules.HasContent(request.ContentJson, request.PlainTextPreview);
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
        NoteValidationRules.HasContent(request.ContentJson, request.PlainTextPreview);
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

    /// <summary>Version 2 previews come from the server, so only version 1 counts the client's preview.</summary>
    public static bool HasContent(RichNoteDocument? document, string? clientPreview) => document?.Version switch
    {
        RichNoteV2Schema.Version => (document.Content?.Count ?? 0) > 0,
        _ => !string.IsNullOrWhiteSpace(clientPreview) || (document?.Blocks?.Count ?? 0) > 0
    };

    public static bool BeValidColor(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        Regex.IsMatch(value, "^(transparent|#([0-9a-fA-F]{6}|[0-9a-fA-F]{8}))$", RegexOptions.CultureInvariant);
}
