using System.Text.Json;
using System.Text.Json.Serialization;

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

/// <summary>A version 2 mark: bold, italic, underline, strike, code, link, textStyle or highlight.</summary>
public sealed record RichNoteMarkV2(
    string Type,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, JsonElement>? Attrs = null);

/// <summary>A version 2 node: ProseMirror-shaped JSON restricted to the ANITIME allow-list (see RichNoteV2Schema).</summary>
public sealed record RichNoteNodeV2(
    string Type,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, JsonElement>? Attrs = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<RichNoteNodeV2>? Content = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<RichNoteMarkV2>? Marks = null);

/// <summary>Version 1 carries <see cref="Blocks"/>; version 2 carries <see cref="Content"/> (the children of the document node).</summary>
public sealed record RichNoteDocument(
    int Version,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyCollection<RichTextBlockNode>? Blocks,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<RichNoteNodeV2>? Content = null);

/// <param name="VersionToken">Ignored. Accepted because installed app builds send their draft token on create.</param>
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
    bool IsArchived,
    string? VersionToken = null);

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

/// <summary>The note request and document types whose JSON must not carry unknown members (they give 400, never a silent drop).</summary>
public static class NoteContractTypes
{
    public static readonly IReadOnlySet<Type> Strict = new HashSet<Type>
    {
        typeof(CreateNoteRequest), typeof(UpdateNoteRequest), typeof(UpdateNoteContentRequest),
        typeof(RichNoteDocument), typeof(RichTextBlockNode), typeof(RichTextNode), typeof(RichTextTextNode), typeof(RichTextMark),
        typeof(RichNoteNodeV2), typeof(RichNoteMarkV2)
    };
}
