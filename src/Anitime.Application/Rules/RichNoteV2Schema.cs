using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Anitime.Contracts;

namespace Anitime.Application.Rules;

/// <summary>A schema violation in a version 2 note, with the JSON-style path of the offending node.</summary>
public sealed record RichNoteSchemaError(string Path, string Message);

/// <summary>
/// The ANITIME allow-list for version 2 notes (ProseMirror-shaped JSON with Tiptap names), and the server-side preview.
/// Source of truth: docs/NOTES-PARITY.md §3.2 in the app repo. Anything not listed here is rejected.
/// </summary>
public static class RichNoteV2Schema
{
    public const int Version = 2;
    public const int MaxTopLevelNodes = 5_000;
    public const int MaxNodes = 50_000;
    public const int MaxTextLength = 20_000;
    public const int MaxListDepth = 8;
    public const int MaxTableColumns = 50;
    public const int MaxTableRows = 1_000;
    public const int MaxCellSpan = 20;
    public const int MaxImages = 100;
    public const int MaxContentBytes = 2 * 1024 * 1024;
    public const int MaxOrderedListStart = 100_000;
    public const int MaxImageDimension = 20_000;
    public const int MaxColumnWidth = 10_000;
    public const int MaxAltLength = 500;
    public const int MaxHrefLength = 2_048;
    public const int MaxFontFamilyLength = 64;

    private const int MaxReportedErrors = 20;

    public static readonly IReadOnlySet<string> Palette = new HashSet<string>(StringComparer.Ordinal) { "pink", "purple", "orange", "mint", "blue" };

    private static readonly HashSet<string> BlockTypes = new(StringComparer.Ordinal)
    {
        "paragraph", "heading", "codeBlock", "blockquote", "bulletList", "orderedList", "taskList", "table", "horizontalRule", "image"
    };

    private static readonly HashSet<string> ListTypes = new(StringComparer.Ordinal) { "bulletList", "orderedList", "taskList" };
    private static readonly HashSet<string> TextAligns = new(StringComparer.Ordinal) { "left", "center", "right", "justify" };
    private static readonly HashSet<string> ListStyles = new(StringComparer.Ordinal) { "bullet", "dash" };
    private static readonly HashSet<string> PlainMarks = new(StringComparer.Ordinal) { "bold", "italic", "underline", "strike", "code" };

    private static readonly Regex HexColor = new("^#([0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", RegexOptions.CultureInvariant);
    private static readonly Regex FontSize = new(@"^\d{1,3}(px)?$", RegexOptions.CultureInvariant);
    private static readonly Regex LinkScheme = new("^(https?:|mailto:|tel:|anitime-note:)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private const string NoteLinkScheme = "anitime-note:";

    public static IReadOnlyList<RichNoteSchemaError> Validate(IReadOnlyList<RichNoteNodeV2> content)
    {
        var walker = new Walker();
        walker.Run(content);
        return walker.Errors;
    }

    /// <summary>
    /// One line per paragraph, heading and code block (hard breaks become line breaks); one "cell | cell" line per table row;
    /// rules and images add nothing. Trimmed; the caller shortens it to the stored preview length.
    /// </summary>
    public static string BuildPlainText(IReadOnlyList<RichNoteNodeV2> content)
    {
        var lines = new List<string>();
        foreach (var node in content)
        {
            AppendLines(node, lines);
        }

        return string.Join('\n', lines).Trim();
    }

    private static void AppendLines(RichNoteNodeV2 node, List<string> lines)
    {
        switch (node.Type)
        {
            case "paragraph" or "heading" or "codeBlock":
                lines.Add(InlineText(node));
                break;
            case "table":
                foreach (var row in node.Content ?? [])
                {
                    lines.Add(string.Join(" | ", (row.Content ?? []).Select(CellText)));
                }
                break;
            case "horizontalRule" or "image":
                break;
            default:
                foreach (var child in node.Content ?? [])
                {
                    AppendLines(child, lines);
                }
                break;
        }
    }

    private static string CellText(RichNoteNodeV2 cell)
    {
        var parts = new List<string>();
        AppendLines(cell, parts);
        return string.Join(' ', parts.Where(part => part.Length > 0));
    }

    private static string InlineText(RichNoteNodeV2 node)
    {
        var builder = new StringBuilder();
        foreach (var child in node.Content ?? [])
        {
            builder.Append(child.Type == "hardBreak" ? "\n" : child.Text);
        }

        return builder.ToString();
    }

    private sealed class Walker
    {
        private int _nodes;
        private int _images;
        private bool _stopped;

        public List<RichNoteSchemaError> Errors { get; } = [];

        public void Run(IReadOnlyList<RichNoteNodeV2> content)
        {
            if (content.Count > MaxTopLevelNodes)
            {
                Fail("content", $"A note can have at most {MaxTopLevelNodes} top-level blocks.");
                return;
            }

            for (var i = 0; i < content.Count && !_stopped; i++)
            {
                var path = $"content[{i}]";
                if (content[i] is null || !BlockTypes.Contains(content[i].Type ?? string.Empty))
                {
                    Fail(path, $"'{content[i]?.Type}' is not allowed at the top level of a note.");
                    continue;
                }

                Visit(content[i], path, listDepth: 0, insideCell: false);
            }
        }

        private void Visit(RichNoteNodeV2 node, string path, int listDepth, bool insideCell)
        {
            if (_stopped)
            {
                return;
            }

            if (++_nodes > MaxNodes)
            {
                Fail("content", $"A note can have at most {MaxNodes} nodes.");
                _stopped = true;
                return;
            }

            if (ListTypes.Contains(node.Type) && ++listDepth > MaxListDepth)
            {
                Fail(path, $"Lists can be nested at most {MaxListDepth} levels deep.");
                return;
            }

            if (node.Type == "image" && ++_images > MaxImages)
            {
                Fail(path, $"A note can have at most {MaxImages} images.");
            }

            CheckTextAndMarks(node, path);
            CheckAttrs(node, path);
            CheckChildren(node, path, listDepth, insideCell || node.Type is "tableCell" or "tableHeader");

            if (node.Type == "table")
            {
                CheckTableShape(node, path);
            }
        }

        private void CheckTextAndMarks(RichNoteNodeV2 node, string path)
        {
            if (node.Type == "text")
            {
                if (string.IsNullOrEmpty(node.Text))
                {
                    Fail(path, "A text node needs non-empty text.");
                }
                else if (node.Text.Length > MaxTextLength)
                {
                    Fail(path, $"A text node can have at most {MaxTextLength} characters.");
                }

                return;
            }

            if (node.Text is not null)
            {
                Fail(path, $"'{node.Type}' can't have text; put text in a child text node.");
            }

            if (node.Marks is not null)
            {
                Fail(path, $"'{node.Type}' can't have marks; only text nodes can.");
            }
        }

        private void CheckChildren(RichNoteNodeV2 node, string path, int listDepth, bool insideCell)
        {
            var content = node.Content;
            var rule = ChildRuleFor(node.Type);

            if (rule == ChildRule.None)
            {
                if (content is not null)
                {
                    Fail(path, $"'{node.Type}' can't have content.");
                }

                return;
            }

            if (content is null || content.Count == 0)
            {
                if (rule is ChildRule.ListItems or ChildRule.TaskItems or ChildRule.Rows or ChildRule.Cells or ChildRule.ItemBody or ChildRule.CellBody)
                {
                    Fail(path, $"'{node.Type}' needs at least one child.");
                }

                return;
            }

            for (var i = 0; i < content.Count && !_stopped; i++)
            {
                var child = content[i];
                var childPath = $"{path}.content[{i}]";
                if (child is null || !IsAllowedChild(rule, child.Type ?? string.Empty, i, insideCell))
                {
                    Fail(childPath, $"'{child?.Type}' is not allowed inside '{node.Type}'.");
                    continue;
                }

                if (rule == ChildRule.CodeText && child.Marks is not null)
                {
                    Fail(childPath, "Text in a code block can't have marks.");
                }

                Visit(child, childPath, listDepth, insideCell);
                if (child.Type == "text")
                {
                    CheckMarks(child, childPath);
                }
            }
        }

        private static ChildRule ChildRuleFor(string type) => type switch
        {
            "paragraph" or "heading" => ChildRule.Inline,
            "codeBlock" => ChildRule.CodeText,
            "blockquote" => ChildRule.Blocks,
            "bulletList" or "orderedList" => ChildRule.ListItems,
            "taskList" => ChildRule.TaskItems,
            "listItem" or "taskItem" => ChildRule.ItemBody,
            "table" => ChildRule.Rows,
            "tableRow" => ChildRule.Cells,
            "tableCell" or "tableHeader" => ChildRule.CellBody,
            _ => ChildRule.None
        };

        private static bool IsAllowedChild(ChildRule rule, string type, int index, bool insideCell) => rule switch
        {
            ChildRule.Inline => type is "text" or "hardBreak",
            ChildRule.CodeText => type == "text",
            ChildRule.Blocks => BlockTypes.Contains(type) && !(insideCell && type == "table"),
            ChildRule.ListItems => type == "listItem",
            ChildRule.TaskItems => type == "taskItem",
            // Like Tiptap: an item starts with a paragraph; nested lists and other blocks follow.
            ChildRule.ItemBody => index == 0 ? type == "paragraph" : BlockTypes.Contains(type) && !(insideCell && type == "table"),
            ChildRule.Rows => type == "tableRow",
            ChildRule.Cells => type is "tableCell" or "tableHeader",
            ChildRule.CellBody => BlockTypes.Contains(type) && type != "table",
            _ => false
        };

        private void CheckAttrs(RichNoteNodeV2 node, string path)
        {
            var attrs = node.Attrs ?? new Dictionary<string, JsonElement>();
            switch (node.Type)
            {
                case "paragraph":
                    Allow(attrs, path, node.Type, "textAlign");
                    Optional(attrs, path, "textAlign", value => IsOneOf(value, TextAligns));
                    break;
                case "heading":
                    Allow(attrs, path, node.Type, "level", "textAlign", "collapsed");
                    Required(attrs, path, "level", value => IsInt(value, 1, 3));
                    Optional(attrs, path, "textAlign", value => IsOneOf(value, TextAligns));
                    Optional(attrs, path, "collapsed", IsBool);
                    break;
                case "bulletList":
                    Allow(attrs, path, node.Type, "listStyle");
                    Optional(attrs, path, "listStyle", value => IsOneOf(value, ListStyles));
                    break;
                case "orderedList":
                    Allow(attrs, path, node.Type, "start");
                    Optional(attrs, path, "start", value => IsInt(value, 1, MaxOrderedListStart));
                    break;
                case "taskItem":
                    Allow(attrs, path, node.Type, "checked");
                    Required(attrs, path, "checked", IsBool);
                    break;
                case "tableCell" or "tableHeader":
                    Allow(attrs, path, node.Type, "colspan", "rowspan", "colwidth");
                    Optional(attrs, path, "colspan", value => IsInt(value, 1, MaxCellSpan));
                    Optional(attrs, path, "rowspan", value => IsInt(value, 1, MaxCellSpan));
                    Optional(attrs, path, "colwidth", IsColumnWidths);
                    break;
                case "image":
                    Allow(attrs, path, node.Type, "assetId", "width", "height", "alt");
                    Required(attrs, path, "assetId", value => value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out _));
                    Optional(attrs, path, "width", value => IsInt(value, 1, MaxImageDimension));
                    Optional(attrs, path, "height", value => IsInt(value, 1, MaxImageDimension));
                    Optional(attrs, path, "alt", value => IsString(value, MaxAltLength));
                    break;
                default:
                    Allow(attrs, path, node.Type);
                    break;
            }
        }

        private void CheckMarks(RichNoteNodeV2 text, string path)
        {
            if (text.Marks is null)
            {
                return;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < text.Marks.Count; i++)
            {
                var mark = text.Marks[i];
                var markPath = $"{path}.marks[{i}]";
                var type = mark?.Type ?? string.Empty;
                if (!seen.Add(type))
                {
                    Fail(markPath, $"Mark '{type}' appears twice on the same text.");
                    continue;
                }

                var attrs = mark?.Attrs ?? new Dictionary<string, JsonElement>();
                if (PlainMarks.Contains(type))
                {
                    Allow(attrs, markPath, type);
                }
                else if (type == "link")
                {
                    Allow(attrs, markPath, type, "href");
                    Required(attrs, markPath, "href", IsAllowedHref);
                }
                else if (type == "textStyle")
                {
                    Allow(attrs, markPath, type, "color", "fontSize", "fontFamily");
                    Optional(attrs, markPath, "color", IsColor);
                    Optional(attrs, markPath, "fontSize", value => value.ValueKind == JsonValueKind.String && FontSize.IsMatch(value.GetString()!));
                    Optional(attrs, markPath, "fontFamily", value => IsString(value, MaxFontFamilyLength));
                }
                else if (type == "highlight")
                {
                    Allow(attrs, markPath, type, "color");
                    Required(attrs, markPath, "color", IsColor);
                }
                else
                {
                    Fail(markPath, $"Mark '{type}' is not allowed.");
                }
            }
        }

        private void CheckTableShape(RichNoteNodeV2 table, string path)
        {
            var rows = table.Content ?? [];
            if (rows.Count > MaxTableRows)
            {
                Fail(path, $"A table can have at most {MaxTableRows} rows.");
                return;
            }

            // Columns still covered by a rowspan from an earlier row count toward each row's width.
            var coveredRows = new List<int>();
            int? expectedWidth = null;
            for (var r = 0; r < rows.Count; r++)
            {
                var width = coveredRows.Count(remaining => remaining > 0);
                var column = 0;
                foreach (var cell in rows[r].Content ?? [])
                {
                    while (column < coveredRows.Count && coveredRows[column] > 0)
                    {
                        column++;
                    }

                    var colspan = SpanOf(cell, "colspan");
                    var rowspan = SpanOf(cell, "rowspan");
                    for (var c = column; c < column + colspan; c++)
                    {
                        while (coveredRows.Count <= c)
                        {
                            coveredRows.Add(0);
                        }

                        coveredRows[c] = rowspan;
                    }

                    column += colspan;
                    width += colspan;
                }

                for (var c = 0; c < coveredRows.Count; c++)
                {
                    coveredRows[c] = Math.Max(0, coveredRows[c] - 1);
                }

                if (width > MaxTableColumns)
                {
                    Fail($"{path}.content[{r}]", $"A table can have at most {MaxTableColumns} columns.");
                    return;
                }

                expectedWidth ??= width;
                if (width != expectedWidth)
                {
                    Fail($"{path}.content[{r}]", $"Every table row needs the same number of columns ({expectedWidth}); this one has {width}.");
                    return;
                }
            }
        }

        private static int SpanOf(RichNoteNodeV2 cell, string key) =>
            cell.Attrs is not null && cell.Attrs.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out var span) && span is >= 1 and <= MaxCellSpan
                ? span
                : 1;

        private void Allow(IReadOnlyDictionary<string, JsonElement> attrs, string path, string type, params string[] allowed)
        {
            foreach (var key in attrs.Keys)
            {
                if (!allowed.Contains(key, StringComparer.Ordinal))
                {
                    Fail($"{path}.attrs.{key}", $"Attribute '{key}' is not allowed on '{type}'.");
                }
            }
        }

        private void Required(IReadOnlyDictionary<string, JsonElement> attrs, string path, string key, Func<JsonElement, bool> isValid)
        {
            if (!attrs.TryGetValue(key, out var value) || !isValid(value))
            {
                Fail($"{path}.attrs.{key}", $"Attribute '{key}' is missing or invalid.");
            }
        }

        private void Optional(IReadOnlyDictionary<string, JsonElement> attrs, string path, string key, Func<JsonElement, bool> isValid)
        {
            if (attrs.TryGetValue(key, out var value) && value.ValueKind != JsonValueKind.Null && !isValid(value))
            {
                Fail($"{path}.attrs.{key}", $"Attribute '{key}' is invalid.");
            }
        }

        private void Fail(string path, string message)
        {
            if (Errors.Count < MaxReportedErrors)
            {
                Errors.Add(new RichNoteSchemaError(path, message));
            }
            else
            {
                _stopped = true;
            }
        }

        private enum ChildRule { None, Inline, CodeText, Blocks, ListItems, TaskItems, ItemBody, Rows, Cells, CellBody }
    }

    private static bool IsBool(JsonElement value) => value.ValueKind is JsonValueKind.True or JsonValueKind.False;

    private static bool IsInt(JsonElement value, int min, int max) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number >= min && number <= max;

    // jsonb can't store U+0000, so it is rejected rather than turned into a database error.
    private static bool IsString(JsonElement value, int maxLength) =>
        value.ValueKind == JsonValueKind.String && value.GetString() is { } text && text.Length <= maxLength && !text.Contains('\0');

    private static bool IsOneOf(JsonElement value, IReadOnlySet<string> allowed) =>
        value.ValueKind == JsonValueKind.String && allowed.Contains(value.GetString()!);

    private static bool IsColor(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && value.GetString() is { } color && (Palette.Contains(color) || HexColor.IsMatch(color));

    private static bool IsColumnWidths(JsonElement value) =>
        value.ValueKind == JsonValueKind.Array && value.GetArrayLength() <= MaxCellSpan &&
        value.EnumerateArray().All(width => IsInt(width, 0, MaxColumnWidth));

    private static bool IsAllowedHref(JsonElement value)
    {
        if (!IsString(value, MaxHrefLength))
        {
            return false;
        }

        var href = value.GetString()!;
        if (!LinkScheme.IsMatch(href))
        {
            return false;
        }

        return !href.StartsWith(NoteLinkScheme, StringComparison.OrdinalIgnoreCase) ||
            Guid.TryParse(href[NoteLinkScheme.Length..], out _);
    }
}
