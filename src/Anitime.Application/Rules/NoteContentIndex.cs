using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Anitime.Contracts;

namespace Anitime.Application.Rules;

/// <summary>What a note's content says about it: typed #tags, note links, image ids and whether it has a checklist or image.</summary>
public sealed record NoteContentFacts(
    IReadOnlyList<string> Tags,
    IReadOnlyList<Guid> LinkedNoteIds,
    IReadOnlyList<Guid> AssetIds,
    bool HasChecklist,
    bool HasImage)
{
    public static readonly NoteContentFacts Empty = new([], [], [], false, false);
}

/// <summary>
/// Derives <see cref="NoteContentFacts"/> on every content save. Tags and links come from version 2 only;
/// version 1 contributes its checklist flag. Tags are one word of letters, digits, '_' or '-', with at least one letter
/// (Apple's rule), lowercased; text in code, code blocks and links is skipped.
/// </summary>
public static partial class NoteContentIndex
{
    public const int MaxTagLength = 64;
    public const int MaxExplicitTags = 50;
    public const int MaxTagsPerNote = 100;
    private const string NoteLinkScheme = "anitime-note:";

    public static NoteContentFacts Analyze(RichNoteDocument document) =>
        document.Version == RichNoteV2Schema.Version && document.Content is not null
            ? AnalyzeV2(document.Content)
            : new NoteContentFacts([], [], [], HasV1Checklist(document.Blocks), false);

    /// <summary>Returns the normalized tag ("#Work" → "work"), or null when it isn't a valid tag.</summary>
    public static string? NormalizeTag(string? raw)
    {
        var tag = raw?.Trim().TrimStart('#').ToLowerInvariant();
        return tag is { Length: > 0 and <= MaxTagLength } && TagBody().IsMatch(tag) && tag.Any(char.IsLetter) ? tag : null;
    }

    public static List<string> MergeTags(IEnumerable<string> parsed, IEnumerable<string> explicitTags) =>
        parsed.Concat(explicitTags).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(MaxTagsPerNote).ToList();

    public static IEnumerable<string> ParseTags(string text) =>
        InlineTag().Matches(text).Select(match => NormalizeTag(match.Groups[1].Value.TrimEnd('-'))).OfType<string>();

    private static NoteContentFacts AnalyzeV2(IReadOnlyList<RichNoteNodeV2> content)
    {
        var walker = new V2Walker();
        foreach (var node in content)
        {
            walker.Visit(node);
        }

        return new NoteContentFacts(
            walker.Tags.Order(StringComparer.Ordinal).ToList(),
            walker.Links.ToList(),
            walker.Assets.ToList(),
            walker.HasChecklist,
            walker.Assets.Count > 0);
    }

    private sealed class V2Walker
    {
        public HashSet<string> Tags { get; } = new(StringComparer.Ordinal);
        public HashSet<Guid> Links { get; } = [];
        public HashSet<Guid> Assets { get; } = [];
        public bool HasChecklist { get; private set; }

        public void Visit(RichNoteNodeV2 node)
        {
            switch (node.Type)
            {
                case "taskList":
                    HasChecklist = true;
                    break;
                case "image" when node.Attrs is not null && node.Attrs.TryGetValue("assetId", out var assetId) &&
                                  assetId.ValueKind == JsonValueKind.String && Guid.TryParse(assetId.GetString(), out var id):
                    Assets.Add(id);
                    return;
                case "paragraph" or "heading":
                    ReadInline(node);
                    return;
                case "codeBlock":
                    return;
            }

            foreach (var child in node.Content ?? [])
            {
                Visit(child);
            }
        }

        private void ReadInline(RichNoteNodeV2 block)
        {
            var text = new StringBuilder();
            foreach (var inline in block.Content ?? [])
            {
                var marks = inline.Marks ?? [];
                foreach (var mark in marks)
                {
                    if (mark.Type == "link" && mark.Attrs is not null && mark.Attrs.TryGetValue("href", out var href) &&
                        href.ValueKind == JsonValueKind.String && href.GetString() is { } value &&
                        value.StartsWith(NoteLinkScheme, StringComparison.OrdinalIgnoreCase) &&
                        Guid.TryParse(value[NoteLinkScheme.Length..], out var linked))
                    {
                        Links.Add(linked);
                    }
                }

                // A space keeps skipped runs from gluing their neighbours into one tag.
                var skip = inline.Type != "text" || marks.Any(mark => mark.Type is "code" or "link");
                text.Append(skip ? " " : inline.Text);
            }

            foreach (var tag in ParseTags(text.ToString()))
            {
                Tags.Add(tag);
            }
        }
    }

    private static bool HasV1Checklist(IEnumerable<RichTextBlockNode>? blocks) =>
        blocks?.Any(block => block is not null &&
            (block.Type == "checkboxListItem" ||
             HasV1Checklist(block.Children?.Select(child => child?.BlockNode).OfType<RichTextBlockNode>()))) == true;

    [GeneratedRegex(@"^[\p{L}\p{M}\p{N}_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex TagBody();

    [GeneratedRegex(@"(?<![\p{L}\p{M}\p{N}_#&/])#([\p{L}\p{M}\p{N}_-]{1,64})(?![\p{L}\p{M}\p{N}_])", RegexOptions.CultureInvariant)]
    private static partial Regex InlineTag();
}
