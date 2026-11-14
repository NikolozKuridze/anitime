using Anitime.Application.Rules;
using Anitime.Contracts;
using FluentValidation;

namespace Anitime.Application.Validation;

public static class NoteLibraryRules
{
    public const int MaxNameLength = 100;
    public const int MaxMoveBatch = 500;
    public const int MaxFilterTags = 20;
    public const string AnyTags = "any";
    public const string AllTags = "all";
}

public sealed class CreateNoteFolderRequestValidator : AbstractValidator<CreateNoteFolderRequest>
{
    public CreateNoteFolderRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().Must(name => !string.IsNullOrWhiteSpace(name)).MaximumLength(NoteLibraryRules.MaxNameLength);
    }
}

public sealed class UpdateNoteFolderRequestValidator : AbstractValidator<UpdateNoteFolderRequest>
{
    public UpdateNoteFolderRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().Must(name => !string.IsNullOrWhiteSpace(name)).MaximumLength(NoteLibraryRules.MaxNameLength);
    }
}

public sealed class MoveNotesRequestValidator : AbstractValidator<MoveNotesRequest>
{
    public MoveNotesRequestValidator()
    {
        RuleFor(x => x.NoteIds).NotNull().Must(ids => ids.Count is > 0 and <= NoteLibraryRules.MaxMoveBatch)
            .WithMessage($"NoteIds must list 1 to {NoteLibraryRules.MaxMoveBatch} notes.");
    }
}

public sealed class SaveSmartFolderRequestValidator : AbstractValidator<SaveSmartFolderRequest>
{
    public SaveSmartFolderRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().Must(name => !string.IsNullOrWhiteSpace(name)).MaximumLength(NoteLibraryRules.MaxNameLength);
        RuleFor(x => x.Filter).NotNull();
        RuleFor(x => x.Filter.Tags)
            .Must(tags => tags!.Count <= NoteLibraryRules.MaxFilterTags && tags.All(tag => NoteContentIndex.NormalizeTag(tag) is not null))
            .WithMessage($"Filter tags must be at most {NoteLibraryRules.MaxFilterTags} valid tags.")
            .When(x => x.Filter?.Tags is not null);
        RuleFor(x => x.Filter.TagMode)
            .Must(mode => mode is null or NoteLibraryRules.AnyTags or NoteLibraryRules.AllTags)
            .WithMessage("TagMode must be any or all.")
            .When(x => x.Filter is not null);
        RuleFor(x => x.Filter).Must(f => f.CreatedFrom is null || f.CreatedTo is null || f.CreatedFrom <= f.CreatedTo)
            .WithMessage("CreatedFrom must not be after CreatedTo.").When(x => x.Filter is not null);
        RuleFor(x => x.Filter).Must(f => f.EditedFrom is null || f.EditedTo is null || f.EditedFrom <= f.EditedTo)
            .WithMessage("EditedFrom must not be after EditedTo.").When(x => x.Filter is not null);
    }
}

public sealed class ConflictCopyRequestValidator : AbstractValidator<ConflictCopyRequest>
{
    public ConflictCopyRequestValidator()
    {
        RuleFor(x => x.ContentJson).NotNull().SetValidator(new RichNoteDocumentValidator());
        RuleFor(x => x.Title).MaximumLength(160);
    }
}
