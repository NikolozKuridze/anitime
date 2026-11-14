using Anitime.Contracts;

namespace Anitime.Api.Infrastructure;

/// <summary>Builds search snippets and match ranges (UTF-16 indices, the same as JavaScript string indices).</summary>
public static class NoteSearchSnippets
{
    private const int ContextBefore = 40;
    private const int SnippetLength = 160;

    public static IReadOnlyList<TextRange> Ranges(string text, IReadOnlyList<string> terms)
    {
        var ranges = new List<TextRange>();
        foreach (var term in terms)
        {
            for (var index = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
                 index >= 0;
                 index = text.IndexOf(term, index + term.Length, StringComparison.OrdinalIgnoreCase))
            {
                ranges.Add(new TextRange(index, term.Length));
            }
        }

        return Merge(ranges);
    }

    /// <summary>About 160 characters around the first match (line breaks shown as spaces), or the start of the text.</summary>
    public static (string Snippet, IReadOnlyList<TextRange> Ranges) Snippet(string text, IReadOnlyList<string> terms)
    {
        var first = terms
            .Select(term => text.IndexOf(term, StringComparison.OrdinalIgnoreCase))
            .Where(index => index >= 0)
            .DefaultIfEmpty(0)
            .Min();

        var start = Math.Max(0, first - ContextBefore);
        var end = Math.Min(text.Length, start + SnippetLength);
        if (start > 0 && char.IsLowSurrogate(text[start]))
        {
            start--;
        }

        if (end < text.Length && char.IsLowSurrogate(text[end]))
        {
            end++;
        }

        var snippet = text[start..end].Replace('\n', ' ').Replace('\r', ' ');
        return (snippet, Ranges(snippet, terms));
    }

    private static List<TextRange> Merge(List<TextRange> ranges)
    {
        var merged = new List<TextRange>();
        foreach (var range in ranges.OrderBy(r => r.Start))
        {
            if (merged.Count > 0 && range.Start <= merged[^1].Start + merged[^1].Length)
            {
                var last = merged[^1];
                merged[^1] = last with { Length = Math.Max(last.Start + last.Length, range.Start + range.Length) - last.Start };
            }
            else
            {
                merged.Add(range);
            }
        }

        return merged;
    }
}
