using System.Text.Json;
using Anitime.Application.Rules;
using Anitime.Contracts;

namespace Anitime.Application.Tests;

public sealed class RichNoteV2SchemaTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static IReadOnlyList<RichNoteNodeV2> Parse(string contentArray) =>
        JsonSerializer.Deserialize<List<RichNoteNodeV2>>(contentArray, Web)!;

    private static IReadOnlyList<RichNoteNodeV2> LinkDocument(string href) =>
        Parse("""[{"type":"paragraph","content":[{"type":"text","text":"a","marks":[{"type":"link","attrs":{"href":""" + JsonSerializer.Serialize(href) + "}}]}]}]");

    [Fact]
    public void Preview_TableCellsJoinTheirBlocks_AndListsGiveOneLinePerParagraph()
    {
        var content = Parse("""
            [{"type":"table","content":[{"type":"tableRow","content":[
               {"type":"tableCell","content":[{"type":"paragraph","content":[{"type":"text","text":"a"}]},{"type":"bulletList","content":[{"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"b"}]}]}]}]},
               {"type":"tableCell","content":[{"type":"paragraph"}]},
               {"type":"tableHeader","content":[{"type":"paragraph","content":[{"type":"text","text":"c"}]}]}]}]},
             {"type":"taskList","content":[{"type":"taskItem","attrs":{"checked":true},"content":[{"type":"paragraph","content":[{"type":"text","text":"x"}]},{"type":"paragraph","content":[{"type":"text","text":"y"}]}]}]},
             {"type":"horizontalRule"},
             {"type":"paragraph","content":[{"type":"text","text":"  "}]}]
            """);

        Assert.Equal("a b |  | c\nx\ny", RichNoteV2Schema.BuildPlainText(content));
    }

    [Fact]
    public void ValidDocument_HasNoErrors()
    {
        var content = Parse("""[{"type":"heading","attrs":{"level":2,"textAlign":null,"collapsed":true},"content":[{"type":"text","text":"ქართული 😀","marks":[{"type":"highlight","attrs":{"color":"pink"}}]}]}]""");

        Assert.Empty(RichNoteV2Schema.Validate(content));
    }

    [Fact]
    public void Errors_CarryTheNodePath()
    {
        var content = Parse("""[{"type":"paragraph"},{"type":"bulletList","content":[{"type":"listItem","content":[{"type":"paragraph","attrs":{"level":1}}]}]}]""");

        var error = Assert.Single(RichNoteV2Schema.Validate(content));
        Assert.Equal("content[1].content[0].content[0].attrs.level", error.Path);
    }

    [Fact]
    public void ManyErrors_AreCappedAt20()
    {
        var content = Parse($"[{string.Join(",", Enumerable.Repeat("""{"type":"video"}""", 500))}]");

        Assert.Equal(20, RichNoteV2Schema.Validate(content).Count);
    }

    [Theory]
    [InlineData("HTTPS://anitime.space")]
    [InlineData("mailto:a@b.c")]
    [InlineData("tel:+995")]
    [InlineData("anitime-note:3f2504e0-4f89-11d3-9a0c-0305e82c3301")]
    public void AllowedLinkSchemes_Pass(string href)
    {
        var content = LinkDocument(href);

        Assert.Empty(RichNoteV2Schema.Validate(content));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData(" https://x")]
    [InlineData("file:///etc/passwd")]
    [InlineData("//evil.example")]
    [InlineData("anitime-note:")]
    public void OtherLinkSchemes_Fail(string href)
    {
        var content = LinkDocument(href);

        Assert.NotEmpty(RichNoteV2Schema.Validate(content));
    }
}
