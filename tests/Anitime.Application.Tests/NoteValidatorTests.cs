using Anitime.Application.Validation;
using Anitime.Contracts;
using FluentValidation.TestHelper;

namespace Anitime.Application.Tests;

public sealed class NoteValidatorTests
{
    private readonly CreateNoteRequestValidator _createValidator = new();
    private readonly UpdateNoteContentRequestValidator _contentValidator = new();
    private readonly UpdateNoteRequestValidator _updateValidator = new();

    [Fact]
    public void CreateNote_ShouldRequireTitleOrContent()
    {
        var model = new CreateNoteRequest(
            "",
            new RichNoteDocument(1, []),
            "",
            "Georgia",
            16,
            400,
            "#111827",
            "#FFFFFF",
            false,
            false);

        var result = _createValidator.TestValidate(model);

        result.ShouldHaveAnyValidationError();
    }

    [Fact]
    public void CreateNote_ShouldRejectInvalidColors()
    {
        var model = new CreateNoteRequest(
            "Planning",
            new RichNoteDocument(1, [Paragraph("Some content")]),
            "Some content",
            "Georgia",
            16,
            400,
            "blue",
            "paper",
            false,
            false);

        var result = _createValidator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.TextColor);
        result.ShouldHaveValidationErrorFor(x => x.BackgroundColor);
    }

    [Fact]
    public void PatchContent_ShouldRequireVersionToken()
    {
        var model = new UpdateNoteContentRequest(
            new RichNoteDocument(1, [Paragraph("Autosave body")]),
            "Autosave body",
            "");

        var result = _contentValidator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.VersionToken);
    }

    [Fact]
    public void CreateNote_NullContent_ReportsErrorInsteadOfThrowing()
    {
        var result = _createValidator.TestValidate(new CreateNoteRequest(
            "Title", null!, null, "system-ui", 16, 400, null, null, false, false));

        result.ShouldHaveValidationErrorFor(x => x.ContentJson);
    }

    [Fact]
    public void CreateNote_NullBlocks_ReportsErrorInsteadOfThrowing()
    {
        var result = _createValidator.TestValidate(new CreateNoteRequest(
            "Title", new RichNoteDocument(1, null!), null, "system-ui", 16, 400, null, null, false, false));

        result.ShouldHaveValidationErrorFor("ContentJson.Blocks");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void CreateNote_FontFamilyIsOptional(string? fontFamily)
    {
        var result = _createValidator.TestValidate(new CreateNoteRequest(
            "Title", new RichNoteDocument(1, [Paragraph("x")]), "x", fontFamily, 16, 400, null, null, false, false));

        result.ShouldNotHaveValidationErrorFor(x => x.FontFamily);
    }

    [Fact]
    public void UpdateNote_MetadataOnly_IsValid()
    {
        var result = _updateValidator.TestValidate(new UpdateNoteRequest(
            "Renamed", null, null, "system-ui", 16, 400, "#111827", "#FFFFFF", true, false, "token"));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void UpdateNote_PreviewWithoutContent_IsInvalid()
    {
        var result = _updateValidator.TestValidate(new UpdateNoteRequest(
            "Renamed", null, "preview", "system-ui", 16, 400, null, null, false, false, "token"));

        result.ShouldHaveValidationErrorFor(x => x.ContentJson);
    }

    [Fact]
    public void UpdateNote_WrongDocumentVersion_IsInvalid()
    {
        var result = _updateValidator.TestValidate(new UpdateNoteRequest(
            "Title", new RichNoteDocument(2, []), null, "system-ui", 16, 400, null, null, false, false, "token"));

        result.ShouldHaveValidationErrorFor("ContentJson.Version");
    }

    private static RichTextBlockNode Paragraph(string text) =>
        new(
            "paragraph",
            "left",
            [new RichTextNode(new RichTextTextNode("text", text), null)]);
}
