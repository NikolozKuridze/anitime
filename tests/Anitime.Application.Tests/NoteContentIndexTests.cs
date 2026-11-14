using System.Text.Json;
using Anitime.Application.Rules;
using Anitime.Contracts;

namespace Anitime.Application.Tests;

public sealed class NoteContentIndexTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static RichNoteDocument V2(string contentArray) =>
        new(2, null, JsonSerializer.Deserialize<List<RichNoteNodeV2>>(contentArray, Web));

    [Theory]
    [InlineData("#Work", "work")]
    [InlineData("work", "work")]
    [InlineData("#ქართული", "ქართული")]
    [InlineData("#a_b-c", "a_b-c")]
    [InlineData("#123", null)]
    [InlineData("#", null)]
    [InlineData("two words", null)]
    [InlineData("#emoji😀", null)]
    public void NormalizeTag(string raw, string? expected) => Assert.Equal(expected, NoteContentIndex.NormalizeTag(raw));

    [Theory]
    [InlineData("#a#b", new[] { "a" })]
    [InlineData("x #end-", new[] { "end" })]
    [InlineData("&#39; html entity", new string[0])]
    [InlineData("https://x.y/#anchor", new string[0])]
    [InlineData("(#paren) #dot.", new[] { "paren", "dot" })]
    public void ParseTags(string text, string[] expected) => Assert.Equal(expected, NoteContentIndex.ParseTags(text));

    [Fact]
    public void Analyze_FindsLinksAssetsChecklistsAndTagsInsideNestedBlocks()
    {
        var target = Guid.NewGuid();
        var asset = Guid.NewGuid();
        var doc = V2($$$"""
            [{"type":"table","content":[{"type":"tableRow","content":[{"type":"tableCell","content":[
               {"type":"paragraph","content":[{"type":"text","text":"#in"},{"type":"text","text":"cell"}]}]}]}]},
             {"type":"blockquote","content":[{"type":"taskList","content":[{"type":"taskItem","attrs":{"checked":true},"content":[
               {"type":"paragraph","content":[{"type":"text","text":"x","marks":[{"type":"link","attrs":{"href":"ANITIME-NOTE:{{{target}}}"}}]}]}]}]}]},
             {"type":"image","attrs":{"assetId":"{{{asset}}}"}}]
            """);

        var facts = NoteContentIndex.Analyze(doc);

        Assert.Equal(["incell"], facts.Tags); // adjacent text nodes are one word
        Assert.Equal([target], facts.LinkedNoteIds);
        Assert.Equal([asset], facts.AssetIds);
        Assert.True(facts.HasChecklist);
        Assert.True(facts.HasImage);
    }

    [Fact]
    public void Analyze_V1_DetectsNestedChecklistsOnly()
    {
        var nested = new RichTextBlockNode("bulletListItem", "left",
            [new RichTextNode(null, new RichTextBlockNode("checkboxListItem", "left", [], false))]);

        var facts = NoteContentIndex.Analyze(new RichNoteDocument(1, [nested]));

        Assert.True(facts.HasChecklist);
        Assert.Empty(facts.Tags);
    }

    [Fact]
    public void MergeTags_IsDistinctAndOrdinalSorted() =>
        Assert.Equal(["a", "b", "ქ"], NoteContentIndex.MergeTags(["ქ", "b"], ["a", "b"]));
}
