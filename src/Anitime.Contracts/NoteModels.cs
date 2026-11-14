namespace Anitime.Contracts;

public sealed record RichTextMark(
    string Type,
    string? Value = null);

public sealed record RichTextTextNode(
    string Type,
    string Text,
    IReadOnlyCollection<RichTextMark>? Marks = null);

public sealed record RichTextBlockNode(
    string Type,
    string? Align,
    IReadOnlyCollection<RichTextNode> Children,
    bool? Checked = null);

public sealed record RichTextNode(
    RichTextTextNode? TextNode = null,
    RichTextBlockNode? BlockNode = null);

public sealed record RichNoteDocument(
    int Version,
    IReadOnlyCollection<RichTextBlockNode> Blocks);

public sealed record CreateNoteRequest(
    string? Title,
    RichNoteDocument ContentJson,
    string? PlainTextPreview,
    string? FontFamily,
    int FontSize,
    int FontWeight,
    string? TextColor,
    string? BackgroundColor,
    bool IsPinned,
    bool IsArchived);

/// <param name="ContentJson">When omitted together with PlainTextPreview, the stored content is kept (metadata-only update).</param>
public sealed record UpdateNoteRequest(
    string? Title,
    RichNoteDocument? ContentJson,
    string? PlainTextPreview,
    string? FontFamily,
    int FontSize,
    int FontWeight,
    string? TextColor,
    string? BackgroundColor,
    bool IsPinned,
    bool IsArchived,
    string VersionToken);

public sealed record UpdateNoteContentRequest(
    RichNoteDocument ContentJson,
    string? PlainTextPreview,
    string VersionToken);

/// <summary>A list row without <c>contentJson</c>, returned by <c>GET /api/notes?view=summary</c>; fetch the full note to edit it.</summary>
public sealed record NoteSummaryResponse(
    Guid Id,
    string Title,
    string PlainTextPreview,
    string FontFamily,
    int FontSize,
    int FontWeight,
    string TextColor,
    string BackgroundColor,
    bool IsPinned,
    bool IsArchived,
    string VersionToken,
    DateTime? LastEditedAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record NoteResponse(
    Guid Id,
    string Title,
    RichNoteDocument ContentJson,
    string PlainTextPreview,
    string FontFamily,
    int FontSize,
    int FontWeight,
    string TextColor,
    string BackgroundColor,
    bool IsPinned,
    bool IsArchived,
    string VersionToken,
    DateTime? LastEditedAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
