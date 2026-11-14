using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;

namespace Anitime.Api.Tests;

/// <summary>Notes P0: strict contracts, document version 2, the old-client guard and the v1 backup (docs/NOTES-PARITY.md in the app repo).</summary>
[Collection(ApiCollection.Name)]
public sealed class NotesV2ApiTests(ApiFixture fixture)
{
    // ---- payloads ----

    private const string V1Content =
        """{"version":1,"blocks":[{"type":"paragraph","align":"left","children":[{"textNode":{"type":"text","text":"old"}}]}]}""";

    private static string Metadata(string? versionToken = null) =>
        "\"title\":\"T\",\"fontFamily\":\"system-ui\",\"fontSize\":16,\"fontWeight\":400,\"textColor\":\"#1F2937\",\"backgroundColor\":\"#FFFFFF\",\"isPinned\":false,\"isArchived\":false" +
        (versionToken is null ? "" : $",\"versionToken\":\"{versionToken}\"");

    /// <summary>A create body: the metadata fields followed by <paramref name="rest"/> (raw JSON members).</summary>
    private static string WithMetadata(string rest) => "{" + Metadata() + "," + rest + "}";

    private static string V2(string contentArray) => $$"""{"version":2,"content":{{contentArray}}}""";

    private static string Paragraph(string text) => $$"""{"type":"paragraph","content":[{"type":"text","text":"{{text}}"}]}""";

    private static string CreateBody(string contentJson, string preview = "client preview") =>
        $$"""{{{Metadata()}},"contentJson":{{contentJson}},"plainTextPreview":"{{preview}}"}""";

    private static string PatchBody(string contentJson, string token) =>
        $$"""{"contentJson":{{contentJson}},"plainTextPreview":"p","versionToken":"{{token}}"}""";

    private static string PutBody(string contentJson, string token) =>
        $$"""{{{Metadata(token)}},"contentJson":{{contentJson}},"plainTextPreview":"p"}""";

    private static StringContent Raw(string json) => new(json, Encoding.UTF8, "application/json");

    // Image nodes must reference the user's own uploads (P2), so the fixed placeholder id is swapped for a real upload.
    private const string PlaceholderAssetId = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";
    private const string PlaceholderAsset = "\"assetId\":\"" + PlaceholderAssetId + "\"";
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<HttpClient, string> UploadedAssets = new();

    private static async Task<string> WithOwnImagesAsync(HttpClient client, string json)
    {
        if (!json.Contains(PlaceholderAsset, StringComparison.Ordinal))
        {
            return json;
        }

        if (!UploadedAssets.TryGetValue(client, out var assetId))
        {
            var file = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13]);
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            var upload = await client.PostAsync("/api/notes/attachments", new MultipartFormDataContent { { file, "file", "p.png" } });
            assetId = (await Json(upload)).GetProperty("assetId").GetString()!;
            UploadedAssets.AddOrUpdate(client, assetId);
        }

        return json.Replace(PlaceholderAsset, "\"assetId\":\"" + assetId + "\"", StringComparison.Ordinal);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string json) =>
        await client.PostAsync("/api/notes", Raw(await WithOwnImagesAsync(client, json)));

    private static async Task<HttpResponseMessage> PatchAsync(HttpClient client, string id, string json) =>
        await client.PatchAsync($"/api/notes/{id}/content", Raw(await WithOwnImagesAsync(client, json)));

    private static async Task<HttpResponseMessage> PutAsync(HttpClient client, string id, string json) =>
        await client.PutAsync($"/api/notes/{id}", Raw(await WithOwnImagesAsync(client, json)));

    private static Task<JsonElement> Json(HttpResponseMessage response) => response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<JsonElement> CreatedAsync(HttpClient client, string contentJson)
    {
        var response = await PostAsync(client, CreateBody(contentJson));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await Json(response);
    }

    private static string Id(JsonElement note) => note.GetProperty("id").GetString()!;
    private static string Token(JsonElement note) => note.GetProperty("versionToken").GetString()!;

    private static async Task<JsonElement> GetAsync(HttpClient client, string id) =>
        await client.GetFromJsonAsync<JsonElement>($"/api/notes/{id}");

    private async Task<int> BackupCountAsync(string noteId)
    {
        var count = 0;
        await fixture.WithDbAsync(async db => count = await db.NoteContentBackups.CountAsync(x => x.NoteId == Guid.Parse(noteId)));
        return count;
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await Json(response)).GetProperty("code").GetString());
    }

    // ---- 1. unknown fields are rejected, never dropped ----

    public static TheoryData<string> UnknownFieldCreateBodies() => new()
    {
        WithMetadata($"\"contentJson\":{V1Content},\"surprise\":1"),
        WithMetadata(""" "contentJson":{"version":1,"blocks":[],"surprise":1},"plainTextPreview":"p" """),
        WithMetadata(""" "contentJson":{"version":1,"blocks":[{"type":"heading","align":"left","level":1,"children":[]}]},"plainTextPreview":"p" """),
        WithMetadata(""" "contentJson":{"version":1,"blocks":[{"type":"paragraph","align":"left","children":[{"textNode":{"type":"text","text":"a","color":"red"}}]}]},"plainTextPreview":"p" """),
        WithMetadata(""" "contentJson":{"version":1,"blocks":[{"type":"paragraph","align":"left","children":[{"textNode":{"type":"text","text":"a","marks":[{"type":"bold","attrs":{}}]}}]}]},"plainTextPreview":"p" """),
        WithMetadata(""" "contentJson":{"version":1,"blocks":[{"type":"paragraph","align":"left","children":[{"imageNode":{}}]}]},"plainTextPreview":"p" """),
        WithMetadata("\"contentJson\":" + V2("""[{"type":"paragraph","content":[],"id":"x"}]""")),
        WithMetadata("\"contentJson\":" + V2("""[{"type":"paragraph","content":[{"type":"text","text":"a","marks":[{"type":"bold","value":"x"}]}]}]"""))
    };

    [Theory]
    [MemberData(nameof(UnknownFieldCreateBodies))]
    public async Task Create_WithUnknownField_Returns400InvalidRequest_AndStoresNothing(string body)
    {
        var client = await fixture.NewUserClientAsync();

        await AssertErrorAsync(await PostAsync(client, body), HttpStatusCode.BadRequest, "invalid_request");

        Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>("/api/notes")).GetArrayLength());
    }

    [Fact]
    public async Task PatchAndPut_WithUnknownField_Return400InvalidRequest_AndKeepContent()
    {
        var client = await fixture.NewUserClientAsync();
        var note = await CreatedAsync(client, V1Content);

        var patch = await PatchAsync(client, Id(note), $$"""{"contentJson":{{V1Content}},"versionToken":"{{Token(note)}}","extra":true}""");
        var put = await PutAsync(client, Id(note), $$"""{{{Metadata(Token(note))}},"contentJson":{{V1Content}},"folderId":"x"}""");

        await AssertErrorAsync(patch, HttpStatusCode.BadRequest, "invalid_request");
        await AssertErrorAsync(put, HttpStatusCode.BadRequest, "invalid_request");
        Assert.Equal(Token(note), Token(await GetAsync(client, Id(note))));
    }

    [Fact]
    public async Task UnknownFieldError_NamesTheField()
    {
        var client = await fixture.NewUserClientAsync();

        var response = await PostAsync(client, $$"""{{{Metadata()}},"contentJson":{{V1Content}},"surprise":1}""");

        Assert.Contains("surprise", await response.Content.ReadAsStringAsync());
    }

    // ---- exact v1 shapes the shipping app sends (App.tsx toNotePayload / toNoteContentPayload / toNoteMetadataPayload) ----

    private const string ShippingV1Content = """
        {"version":1,"blocks":[
          {"type":"heading","align":"left","children":[{"textNode":{"type":"text","text":"Plan"}}]},
          {"type":"paragraph","align":"center","children":[
            {"textNode":{"type":"text","text":"bold","marks":[{"type":"bold"},{"type":"italic"},{"type":"underline"},{"type":"strikeThrough"}]}},
            {"textNode":{"type":"text","text":" link","marks":[{"type":"link","value":"https://anitime.space"}]}},
            {"textNode":{"type":"text","text":" styled","marks":[{"type":"fontSize","value":"18"},{"type":"fontFamily","value":"Georgia"},{"type":"textColor","value":"rgb(255, 0, 0)"},{"type":"backgroundColor","value":"#FFFF00"}]}},
            {"textNode":{"type":"text","text":"\n"}}]},
          {"type":"checkboxListItem","align":"left","checked":true,"children":[
            {"textNode":{"type":"text","text":"done"}},
            {"blockNode":{"type":"checkboxListItem","align":"left","checked":false,"children":[{"textNode":{"type":"text","text":"nested"}}]}}]},
          {"type":"bulletListItem","align":"left","children":[{"textNode":{"type":"text","text":"ქართული 😀"}}]},
          {"type":"orderedListItem","align":"left","children":[]},
          {"type":"blockquote","align":"left","children":[{"textNode":{"type":"text","text":"q","marks":null},"blockNode":null}]}
        ]}
        """;

    [Fact]
    public async Task ShippingAppV1Payloads_AreStillAccepted_OnEveryEndpoint()
    {
        var client = await fixture.NewUserClientAsync();

        // createNote(toNotePayload): versionToken is present only when the draft has one.
        var created = await PostAsync(client, $$"""{{{Metadata("draft-token")}},"contentJson":{{ShippingV1Content}},"plainTextPreview":"Plan"}""");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var note = await Json(created);

        // updateNoteContent({ ...toNoteContentPayload, versionToken })
        var patch = await PatchAsync(client, Id(note), $$"""{"contentJson":{{ShippingV1Content}},"plainTextPreview":"Plan","versionToken":"{{Token(note)}}"}""");
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        // updateNote({ ...metadata, ...content, versionToken }), title omitted when blank
        var put = await PutAsync(client, Id(note), $$"""{"fontFamily":"system-ui","fontSize":16,"fontWeight":400,"textColor":"#1F2937","backgroundColor":"#FFFFFF","isPinned":true,"isArchived":false,"contentJson":{{ShippingV1Content}},"plainTextPreview":"Plan","versionToken":"{{Token(await Json(patch))}}"}""");
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var duplicate = await client.PostAsync($"/api/notes/{Id(note)}/duplicate", Raw("{}"));
        Assert.Equal(HttpStatusCode.Created, duplicate.StatusCode);

        var stored = await GetAsync(client, Id(note));
        Assert.True(stored.GetProperty("contentJson").GetProperty("blocks")[2].GetProperty("checked").GetBoolean());
        Assert.Equal("Plan", stored.GetProperty("plainTextPreview").GetString());
    }

    [Fact]
    public async Task V1Response_KeepsItsExactShape_WithNoV2Property()
    {
        var client = await fixture.NewUserClientAsync();

        var note = await CreatedAsync(client, V1Content);

        var keys = note.GetProperty("contentJson").EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(["version", "blocks"], keys);
    }

    // ---- 2. version 2 ----

    private const string EveryNodeType = """
        [
          {"type":"heading","attrs":{"level":1,"textAlign":"center","collapsed":false},"content":[{"type":"text","text":"Title"}]},
          {"type":"heading","attrs":{"level":2,"collapsed":true},"content":[{"type":"text","text":"Heading"}]},
          {"type":"heading","attrs":{"level":3},"content":[{"type":"text","text":"Sub"}]},
          {"type":"paragraph","attrs":{"textAlign":"justify"},"content":[
            {"type":"text","text":"b","marks":[{"type":"bold"},{"type":"italic"},{"type":"underline"},{"type":"strike"},{"type":"code"}]},
            {"type":"hardBreak"},
            {"type":"text","text":"l","marks":[{"type":"link","attrs":{"href":"https://anitime.space"}}]},
            {"type":"text","text":"m","marks":[{"type":"link","attrs":{"href":"mailto:a@b.c"}}]},
            {"type":"text","text":"t","marks":[{"type":"link","attrs":{"href":"tel:+995555"}}]},
            {"type":"text","text":"n","marks":[{"type":"link","attrs":{"href":"anitime-note:3f2504e0-4f89-11d3-9a0c-0305e82c3301"}}]},
            {"type":"text","text":"s","marks":[{"type":"textStyle","attrs":{"color":"#FF0000","fontSize":"18px","fontFamily":"Georgia"}}]},
            {"type":"text","text":"h","marks":[{"type":"highlight","attrs":{"color":"mint"}}]},
            {"type":"text","text":"x","marks":[{"type":"highlight","attrs":{"color":"#FFFF0080"}}]}
          ]},
          {"type":"paragraph"},
          {"type":"codeBlock","content":[{"type":"text","text":"let x = 1;\nx++"}]},
          {"type":"blockquote","content":[{"type":"paragraph","content":[{"type":"text","text":"quote"}]}]},
          {"type":"bulletList","attrs":{"listStyle":"dash"},"content":[
            {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"dash"}]},
              {"type":"bulletList","content":[{"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"nested"}]}]}]}]}]},
          {"type":"orderedList","attrs":{"start":3},"content":[{"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"three"}]}]}]},
          {"type":"taskList","content":[
            {"type":"taskItem","attrs":{"checked":true},"content":[{"type":"paragraph","content":[{"type":"text","text":"done"}]}]},
            {"type":"taskItem","attrs":{"checked":false},"content":[{"type":"paragraph","content":[{"type":"text","text":"todo"}]}]}]},
          {"type":"table","content":[
            {"type":"tableRow","content":[
              {"type":"tableHeader","attrs":{"colspan":1,"rowspan":1,"colwidth":null},"content":[{"type":"paragraph","content":[{"type":"text","text":"A"}]}]},
              {"type":"tableHeader","content":[{"type":"paragraph","content":[{"type":"text","text":"B"}]}]}]},
            {"type":"tableRow","content":[
              {"type":"tableCell","attrs":{"colspan":2,"colwidth":[120,80]},"content":[{"type":"paragraph","content":[{"type":"text","text":"wide"}]}]}]}]},
          {"type":"horizontalRule"},
          {"type":"image","attrs":{"assetId":"3f2504e0-4f89-11d3-9a0c-0305e82c3301","width":640,"height":480,"alt":"photo"}}
        ]
        """;

    private const string EveryNodeTypePreview =
        "Title\nHeading\nSub\nb\nlmtnshx\n\nlet x = 1;\nx++\nquote\ndash\nnested\nthree\ndone\ntodo\nA | B\nwide";

    [Fact]
    public async Task V2_EveryNodeTypeAndMark_RoundTripsExactly()
    {
        var client = await fixture.NewUserClientAsync();

        var note = await CreatedAsync(client, V2(EveryNodeType));

        var stored = await GetAsync(client, Id(note));
        var content = stored.GetProperty("contentJson");
        Assert.Equal(2, content.GetProperty("version").GetInt32());
        Assert.False(content.TryGetProperty("blocks", out _));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(await WithOwnImagesAsync(client, EveryNodeType)), JsonNode.Parse(content.GetProperty("content").GetRawText())));
    }

    [Fact]
    public async Task V2_PreviewIsDerivedByTheServer_AndClientValueIsIgnored()
    {
        var client = await fixture.NewUserClientAsync();

        var note = await CreatedAsync(client, V2(EveryNodeType));
        Assert.Equal(EveryNodeTypePreview, note.GetProperty("plainTextPreview").GetString());

        var patch = await PatchAsync(client, Id(note), PatchBody(V2($"[{Paragraph("fresh")}]"), Token(note)));
        Assert.Equal("fresh", (await Json(patch)).GetProperty("plainTextPreview").GetString());
    }

    [Fact]
    public async Task V2_Preview_IsCutAt50k_WithoutSplittingAnEmoji()
    {
        var client = await fixture.NewUserClientAsync();
        // Lines: 20,000 + "\n" + 20,000 + "\n" puts the third line at index 40,002; the emoji's high surrogate lands on index 49,999.
        var content = V2($"[{Paragraph(new string('a', 20_000))},{Paragraph(new string('b', 20_000))},{Paragraph(new string('c', 9_997) + "😀d")}]");

        var note = await CreatedAsync(client, content);

        var preview = note.GetProperty("plainTextPreview").GetString()!;
        Assert.Equal(49_999, preview.Length);
        Assert.EndsWith("c", preview);
    }

    public static TheoryData<string, string> InvalidV2Documents() => new()
    {
        { "unknown node type", """[{"type":"video"}]""" },
        { "inline at top level", """[{"type":"text","text":"a"}]""" },
        { "list item at top level", """[{"type":"listItem","content":[]}]""" },
        { "heading level 4", """[{"type":"heading","attrs":{"level":4}}]""" },
        { "heading without level", """[{"type":"heading","content":[{"type":"text","text":"a"}]}]""" },
        { "unknown attr", """[{"type":"paragraph","attrs":{"foo":1}}]""" },
        { "bad textAlign", """[{"type":"paragraph","attrs":{"textAlign":"middle"}}]""" },
        { "collapsed not bool", """[{"type":"heading","attrs":{"level":2,"collapsed":"yes"}}]""" },
        { "unknown mark", """[{"type":"paragraph","content":[{"type":"text","text":"a","marks":[{"type":"blink"}]}]}]""" },
        { "javascript link", """[{"type":"paragraph","content":[{"type":"text","text":"a","marks":[{"type":"link","attrs":{"href":"javascript:alert(1)"}}]}]}]""" },
        { "data link", """[{"type":"paragraph","content":[{"type":"text","text":"a","marks":[{"type":"link","attrs":{"href":"data:text/html,x"}}]}]}]""" },
        { "link without href", """[{"type":"paragraph","content":[{"type":"text","text":"a","marks":[{"type":"link"}]}]}]""" },
        { "note link without guid", """[{"type":"paragraph","content":[{"type":"text","text":"a","marks":[{"type":"link","attrs":{"href":"anitime-note:abc"}}]}]}]""" },
        { "highlight named colour off palette", """[{"type":"paragraph","content":[{"type":"text","text":"a","marks":[{"type":"highlight","attrs":{"color":"red"}}]}]}]""" },
        { "rgb colour", """[{"type":"paragraph","content":[{"type":"text","text":"a","marks":[{"type":"textStyle","attrs":{"color":"rgb(1,2,3)"}}]}]}]""" },
        { "font size em", """[{"type":"paragraph","content":[{"type":"text","text":"a","marks":[{"type":"textStyle","attrs":{"fontSize":"1.2em"}}]}]}]""" },
        { "bold with attrs", """[{"type":"paragraph","content":[{"type":"text","text":"a","marks":[{"type":"bold","attrs":{"x":1}}]}]}]""" },
        { "duplicate mark", """[{"type":"paragraph","content":[{"type":"text","text":"a","marks":[{"type":"bold"},{"type":"bold"}]}]}]""" },
        { "marks in code block", """[{"type":"codeBlock","content":[{"type":"text","text":"a","marks":[{"type":"bold"}]}]}]""" },
        { "hard break in code block", """[{"type":"codeBlock","content":[{"type":"hardBreak"}]}]""" },
        { "marks on a block", """[{"type":"paragraph","marks":[{"type":"bold"}]}]""" },
        { "text on a block", """[{"type":"paragraph","text":"a"}]""" },
        { "text without text", """[{"type":"paragraph","content":[{"type":"text"}]}]""" },
        { "text with content", """[{"type":"paragraph","content":[{"type":"text","text":"a","content":[]}]}]""" },
        { "rule with content", """[{"type":"horizontalRule","content":[{"type":"paragraph"}]}]""" },
        { "block inside paragraph", """[{"type":"paragraph","content":[{"type":"paragraph"}]}]""" },
        { "empty bullet list", """[{"type":"bulletList","content":[]}]""" },
        { "paragraph in bullet list", """[{"type":"bulletList","content":[{"type":"paragraph"}]}]""" },
        { "task item in bullet list", """[{"type":"bulletList","content":[{"type":"taskItem","attrs":{"checked":true},"content":[{"type":"paragraph"}]}]}]""" },
        { "list item not starting with paragraph", """[{"type":"bulletList","content":[{"type":"listItem","content":[{"type":"bulletList","content":[{"type":"listItem","content":[{"type":"paragraph"}]}]}]}]}]""" },
        { "task item without checked", """[{"type":"taskList","content":[{"type":"taskItem","content":[{"type":"paragraph"}]}]}]""" },
        { "bad list style", """[{"type":"bulletList","attrs":{"listStyle":"star"},"content":[{"type":"listItem","content":[{"type":"paragraph"}]}]}]""" },
        { "ordered start 0", """[{"type":"orderedList","attrs":{"start":0},"content":[{"type":"listItem","content":[{"type":"paragraph"}]}]}]""" },
        { "ordered start fraction", """[{"type":"orderedList","attrs":{"start":1.5},"content":[{"type":"listItem","content":[{"type":"paragraph"}]}]}]""" },
        { "cell outside row", """[{"type":"table","content":[{"type":"tableCell","content":[{"type":"paragraph"}]}]}]""" },
        { "table in cell", """[{"type":"table","content":[{"type":"tableRow","content":[{"type":"tableCell","content":[{"type":"table","content":[{"type":"tableRow","content":[{"type":"tableCell","content":[{"type":"paragraph"}]}]}]}]}]}]}]""" },
        { "ragged table", """[{"type":"table","content":[{"type":"tableRow","content":[{"type":"tableCell","content":[{"type":"paragraph"}]},{"type":"tableCell","content":[{"type":"paragraph"}]}]},{"type":"tableRow","content":[{"type":"tableCell","content":[{"type":"paragraph"}]}]}]}]""" },
        { "colspan 21", """[{"type":"table","content":[{"type":"tableRow","content":[{"type":"tableCell","attrs":{"colspan":21},"content":[{"type":"paragraph"}]}]}]}]""" },
        { "empty table", """[{"type":"table","content":[]}]""" },
        { "image without asset", """[{"type":"image","attrs":{"alt":"x"}}]""" },
        { "image bad asset", """[{"type":"image","attrs":{"assetId":"not-a-guid"}}]""" },
        { "image src", """[{"type":"image","attrs":{"assetId":"3f2504e0-4f89-11d3-9a0c-0305e82c3301","src":"https://evil"}}]""" },
        { "NUL in attribute", """[{"type":"image","attrs":{"assetId":"3f2504e0-4f89-11d3-9a0c-0305e82c3301","alt":"a\u0000b"}}]""" },
        { "text over 20k", $$"""[{{Paragraph(new string('a', 20_001))}}]""" }
    };

    [Theory]
    [MemberData(nameof(InvalidV2Documents))]
    public async Task V2_DisallowedShape_Returns400(string reason, string contentArray)
    {
        var client = await fixture.NewUserClientAsync();

        var response = await PostAsync(client, CreateBody(V2(contentArray)));

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{reason}: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>("/api/notes")).GetArrayLength());
    }

    [Theory]
    [InlineData("""{"version":2,"blocks":[]}""")]
    [InlineData("""{"version":2}""")]
    [InlineData("""{"version":2,"content":[],"blocks":[]}""")]
    [InlineData("""{"version":1,"blocks":[],"content":[]}""")]
    [InlineData("""{"version":3,"content":[]}""")]
    public async Task VersionAndBodyMismatch_Returns400(string contentJson)
    {
        var client = await fixture.NewUserClientAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, CreateBody(contentJson))).StatusCode);
    }

    private static string NestedBulletLists(int levels)
    {
        var item = """{"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"deep"}]}]}""";
        var list = $$"""{"type":"bulletList","content":[{{item}}]}""";
        for (var i = 1; i < levels; i++)
        {
            list = $$"""{"type":"bulletList","content":[{"type":"listItem","content":[{"type":"paragraph"},{{list}}]}]}""";
        }

        return $"[{list}]";
    }

    [Fact]
    public async Task V2_ListNesting_AllowsEightLevels_AndRejectsNine()
    {
        var client = await fixture.NewUserClientAsync();

        Assert.Equal(HttpStatusCode.Created, (await PostAsync(client, CreateBody(V2(NestedBulletLists(8))))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, CreateBody(V2(NestedBulletLists(9))))).StatusCode);
    }

    private static string Table(int rows, int columns)
    {
        var cell = """{"type":"tableCell","content":[{"type":"paragraph"}]}""";
        var row = $$"""{"type":"tableRow","content":[{{string.Join(",", Enumerable.Repeat(cell, columns))}}]}""";
        return $$"""[{"type":"table","content":[{{string.Join(",", Enumerable.Repeat(row, rows))}}]}]""";
    }

    [Fact]
    public async Task V2_TableLimits_Are50ColumnsBy1000Rows()
    {
        var client = await fixture.NewUserClientAsync();

        Assert.Equal(HttpStatusCode.Created, (await PostAsync(client, CreateBody(V2(Table(2, 50))))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync(client, CreateBody(V2(Table(1000, 1))))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, CreateBody(V2(Table(2, 51))))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, CreateBody(V2(Table(1001, 1))))).StatusCode);
    }

    [Fact]
    public async Task V2_RowspanCountsTowardTheRowsItCovers()
    {
        var client = await fixture.NewUserClientAsync();
        const string table = """
            [{"type":"table","content":[
              {"type":"tableRow","content":[{"type":"tableCell","attrs":{"rowspan":2},"content":[{"type":"paragraph"}]},{"type":"tableCell","content":[{"type":"paragraph"}]}]},
              {"type":"tableRow","content":[{"type":"tableCell","content":[{"type":"paragraph"}]}]}]}]
            """;

        Assert.Equal(HttpStatusCode.Created, (await PostAsync(client, CreateBody(V2(table)))).StatusCode);
    }

    [Fact]
    public async Task V2_NodeAndImageCountLimits()
    {
        var client = await fixture.NewUserClientAsync();
        var image = """{"type":"image","attrs":{"assetId":"3f2504e0-4f89-11d3-9a0c-0305e82c3301"}}""";
        var text = """{"type":"text","text":"a"}""";

        Assert.Equal(HttpStatusCode.Created, (await PostAsync(client, CreateBody(V2($"[{string.Join(",", Enumerable.Repeat(image, 100))}]")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, CreateBody(V2($"[{string.Join(",", Enumerable.Repeat(image, 101))}]")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, CreateBody(V2($"[{string.Join(",", Enumerable.Repeat(Paragraph("a"), 5_001))}]")))).StatusCode);
        // One paragraph holding 50,000 text nodes is 50,001 nodes in total.
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, CreateBody(V2($$"""[{"type":"paragraph","content":[{{string.Join(",", Enumerable.Repeat(text, 50_000))}}]}]""")))).StatusCode);
    }

    [Fact]
    public async Task V2_ContentOver2MB_Returns413()
    {
        var client = await fixture.NewUserClientAsync();
        var content = V2($"[{string.Join(",", Enumerable.Repeat(Paragraph(new string('a', 20_000)), 110))}]");

        await AssertErrorAsync(await PostAsync(client, CreateBody(content)), HttpStatusCode.RequestEntityTooLarge, "payload_too_large");
    }

    [Fact]
    public async Task V2_EmptyDocumentWithoutTitle_IsAnEmptyNote()
    {
        var client = await fixture.NewUserClientAsync();

        var response = await PostAsync(client, """{"fontSize":16,"fontWeight":400,"isPinned":false,"isArchived":false,"contentJson":{"version":2,"content":[]}}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- 3. old clients can't overwrite a v2 note ----

    [Fact]
    public async Task V1Write_OverV2Note_Returns409NoteFormatTooNew_AndKeepsTheNote()
    {
        var client = await fixture.NewUserClientAsync();
        var note = await CreatedAsync(client, V2(EveryNodeType));

        var patch = await PatchAsync(client, Id(note), PatchBody(V1Content, Token(note)));
        var put = await PutAsync(client, Id(note), PutBody(V1Content, Token(note)));

        await AssertErrorAsync(patch, HttpStatusCode.Conflict, "note_format_too_new");
        await AssertErrorAsync(put, HttpStatusCode.Conflict, "note_format_too_new");
        var stored = await GetAsync(client, Id(note));
        Assert.Equal(Token(note), Token(stored));
        Assert.Equal(2, stored.GetProperty("contentJson").GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task MetadataOnlyPut_OnV2Note_IsAllowed_AndKeepsContent()
    {
        var client = await fixture.NewUserClientAsync();
        var note = await CreatedAsync(client, V2(EveryNodeType));

        var put = await PutAsync(client, Id(note), $"{{{Metadata(Token(note))}}}");

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var stored = await GetAsync(client, Id(note));
        Assert.Equal(2, stored.GetProperty("contentJson").GetProperty("version").GetInt32());
        Assert.Equal(EveryNodeTypePreview, stored.GetProperty("plainTextPreview").GetString());
    }

    [Fact]
    public async Task V1Write_OverV2Note_WithStaleToken_StillFails()
    {
        var client = await fixture.NewUserClientAsync();
        var note = await CreatedAsync(client, V2(EveryNodeType));

        var response = await PatchAsync(client, Id(note), PatchBody(V1Content, "stale"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task V2Writes_OverV2Note_KeepTheVersionTokenRules()
    {
        var client = await fixture.NewUserClientAsync();
        var note = await CreatedAsync(client, V2($"[{Paragraph("one")}]"));

        var ok = await PatchAsync(client, Id(note), PatchBody(V2($"[{Paragraph("two")}]"), Token(note)));
        var stale = await PatchAsync(client, Id(note), PatchBody(V2($"[{Paragraph("three")}]"), Token(note)));

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        await AssertErrorAsync(stale, HttpStatusCode.Conflict, "conflict");
        Assert.Equal("two", (await GetAsync(client, Id(note))).GetProperty("plainTextPreview").GetString());
    }

    [Fact]
    public async Task DuplicateOfV2Note_IsV2()
    {
        var client = await fixture.NewUserClientAsync();
        var note = await CreatedAsync(client, V2(EveryNodeType));

        var copy = await Json(await client.PostAsync($"/api/notes/{Id(note)}/duplicate", Raw("{}")));

        Assert.Equal(2, copy.GetProperty("contentJson").GetProperty("version").GetInt32());
        Assert.Equal(EveryNodeTypePreview, copy.GetProperty("plainTextPreview").GetString());
    }

    // ---- 4. v1 backup on the first v2 write ----

    [Theory]
    [InlineData("patch")]
    [InlineData("put")]
    public async Task FirstV2Write_OverV1Note_BacksUpTheV1Content_Once(string method)
    {
        var client = await fixture.NewUserClientAsync();
        var note = await CreatedAsync(client, V1Content);
        string? storedV1 = null;
        await fixture.WithDbAsync(async db => storedV1 = (await db.Notes.SingleAsync(x => x.Id == Guid.Parse(Id(note)))).ContentJson);

        var first = method == "patch"
            ? await PatchAsync(client, Id(note), PatchBody(V2($"[{Paragraph("new")}]"), Token(note)))
            : await PutAsync(client, Id(note), PutBody(V2($"[{Paragraph("new")}]"), Token(note)));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var second = await PatchAsync(client, Id(note), PatchBody(V2($"[{Paragraph("newer")}]"), Token(await Json(first))));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        await fixture.WithDbAsync(async db =>
        {
            var backup = await db.NoteContentBackups.SingleAsync(x => x.NoteId == Guid.Parse(Id(note)));
            Assert.Equal(1, backup.Version);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(storedV1!), JsonNode.Parse(backup.ContentJson)));
            Assert.Equal("old", JsonDocument.Parse(backup.ContentJson).RootElement.GetProperty("Blocks")[0]
                .GetProperty("Children")[0].GetProperty("TextNode").GetProperty("Text").GetString());
        });
    }

    [Fact]
    public async Task NoteCreatedAsV2_HasNoBackup()
    {
        var client = await fixture.NewUserClientAsync();
        var note = await CreatedAsync(client, V2($"[{Paragraph("a")}]"));

        await PatchAsync(client, Id(note), PatchBody(V2($"[{Paragraph("b")}]"), Token(note)));

        Assert.Equal(0, await BackupCountAsync(Id(note)));
    }

    [Fact]
    public async Task RejectedV2Write_LeavesNoBackup()
    {
        var client = await fixture.NewUserClientAsync();
        var note = await CreatedAsync(client, V1Content);

        await PatchAsync(client, Id(note), PatchBody(V2($"[{Paragraph("b")}]"), "stale"));
        await PatchAsync(client, Id(note), PatchBody(V2("""[{"type":"video"}]"""), Token(note)));

        Assert.Equal(0, await BackupCountAsync(Id(note)));
    }

    [Fact]
    public async Task ParallelFirstV2Writes_ExactlyOneWins_AndOneBackupIsKept()
    {
        var client = await fixture.NewUserClientAsync();
        var note = await CreatedAsync(client, V1Content);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            PatchAsync(client, Id(note), PatchBody(V2($"[{Paragraph($"p{i}")}]"), Token(note)))));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(1, await BackupCountAsync(Id(note)));
    }

    [Fact]
    public async Task V1NoteUpgradedToV2_ThenOldClientSave_Is409_AndV2Survives()
    {
        var client = await fixture.NewUserClientAsync();
        var note = await CreatedAsync(client, V1Content);
        var upgraded = await Json(await PatchAsync(client, Id(note), PatchBody(V2($"[{Paragraph("v2 text")}]"), Token(note))));

        // An old build that loaded the note after the upgrade holds the current token but only speaks v1.
        var oldClient = await PatchAsync(client, Id(note), PatchBody(V1Content, Token(upgraded)));

        await AssertErrorAsync(oldClient, HttpStatusCode.Conflict, "note_format_too_new");
        Assert.Equal("v2 text", (await GetAsync(client, Id(note))).GetProperty("plainTextPreview").GetString());
    }

    // ---- 5. minimum app version ----

    [Fact]
    public async Task AppConfig_IsAnonymous_AndDefaultsToNoMinimum()
    {
        var response = await fixture.AnonymousClient().GetAsync("/api/app/config");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Json(response);
        Assert.Equal(0, body.GetProperty("minIosBuild").GetInt32());
        Assert.Equal("0.0.0", body.GetProperty("minAppVersion").GetString());
    }

    [Fact]
    public async Task AppConfig_ReadsConfiguredMinimums()
    {
        var client = fixture.ClientWithSettings(("ClientApp:MinIosBuild", "42"), ("ClientApp:MinAppVersion", "1.4.0"));

        var body = await client.GetFromJsonAsync<JsonElement>("/api/app/config");

        Assert.Equal(42, body.GetProperty("minIosBuild").GetInt32());
        Assert.Equal("1.4.0", body.GetProperty("minAppVersion").GetString());
    }

    [Fact]
    public async Task Swagger_DocumentsTheV2ExampleAndAppConfig()
    {
        var json = await fixture.AnonymousClient().GetStringAsync("/swagger/v1/swagger.json");

        Assert.Contains("/api/app/config", json);
        Assert.Contains("\"taskItem\"", json);
    }
}
