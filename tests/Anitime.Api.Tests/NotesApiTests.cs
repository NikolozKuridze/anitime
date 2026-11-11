using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Anitime.Domain.Entities;
using Anitime.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Anitime.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class NotesApiTests(ApiFixture fixture)
{
    private static object Doc(string text) => new
    {
        version = 1,
        blocks = new[]
        {
            new { type = "paragraph", align = "left", children = new[] { new { textNode = new { type = "text", text } } } }
        }
    };

    private static Dictionary<string, object?> Body(string text = "december") => new()
    {
        ["title"] = "Title",
        ["contentJson"] = Doc(text),
        ["plainTextPreview"] = text,
        ["fontFamily"] = "system-ui",
        ["fontSize"] = 16,
        ["fontWeight"] = 400,
        ["textColor"] = "#111827",
        ["backgroundColor"] = "#FFFFFF",
        ["isPinned"] = false,
        ["isArchived"] = false
    };

    private static object Content(string text, string? token) =>
        new { contentJson = Doc(text), plainTextPreview = text, versionToken = token };

    private static Dictionary<string, object?> Metadata(string text, string token)
    {
        var body = Body(text);
        body["versionToken"] = token;
        return body;
    }

    private static string Text(JsonElement note) => note.GetProperty("contentJson").GetProperty("blocks")[0]
        .GetProperty("children")[0].GetProperty("textNode").GetProperty("text").GetString()!;

    private static string Token(JsonElement note) => note.GetProperty("versionToken").GetString()!;
    private static string Id(JsonElement note) => note.GetProperty("id").GetString()!;
    private static Task<JsonElement> Json(HttpResponseMessage response) => response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<JsonElement> CreateAsync(HttpClient client, string text = "dec")
    {
        var response = await client.PostAsJsonAsync("/api/notes", Body(text));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await Json(response);
    }

    private static async Task<JsonElement> GetAsync(HttpClient client, string id)
    {
        var response = await client.GetAsync($"/api/notes/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Json(response);
    }

    // ---- success paths ----

    [Fact]
    public async Task Patch_StoresFullText_AndRotatesToken()
    {
        var client = await fixture.NewUserClientAsync();
        var note = await CreateAsync(client, "dec");

        var patch = await client.PatchAsJsonAsync($"/api/notes/{Id(note)}/content", Content("december", Token(note)));

        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var patched = await Json(patch);
        Assert.NotEqual(Token(note), Token(patched));
        var stored = await GetAsync(client, Id(note));
        Assert.Equal("december", Text(stored));
        Assert.Equal("december", stored.GetProperty("plainTextPreview").GetString());
        Assert.Equal(Token(patched), Token(stored));
    }

    [Fact]
    public async Task PatchThenPut_UsingEachReturnedToken_BothSucceed()
    {
        var client = await fixture.NewUserClientAsync();
        var note = await CreateAsync(client);

        var patch = await client.PatchAsJsonAsync($"/api/notes/{Id(note)}/content", Content("december", Token(note)));
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        var put = await client.PutAsJsonAsync($"/api/notes/{Id(note)}", Metadata("december", Token(await Json(patch))));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
    }

    [Fact]
    public async Task RapidSequentialAutosaves_AreNeverRateLimited()
    {
        var client = await fixture.NewUserClientAsync();
        var token = Token(await CreateAsync(client));
        var id = Id(await GetFirstAsync(client));

        for (var i = 0; i < 50; i++)
        {
            var response = await client.PatchAsJsonAsync($"/api/notes/{id}/content", Content($"v{i}", token));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            token = Token(await Json(response));
        }

        Assert.Equal("v49", Text(await GetAsync(client, id)));
    }

    private static async Task<JsonElement> GetFirstAsync(HttpClient client)
    {
        var list = await client.GetFromJsonAsync<JsonElement>("/api/notes");
        return list[0];
    }

    // ---- version token ----

    [Fact]
    public async Task StaleToken_Returns409_AndKeepsStoredContent()
    {
        var client = await fixture.NewUserClientAsync();
        var note = await CreateAsync(client, "first");
        var ok = await client.PatchAsJsonAsync($"/api/notes/{Id(note)}/content", Content("second", Token(note)));
        var current = Token(await Json(ok));

        var stalePatch = await client.PatchAsJsonAsync($"/api/notes/{Id(note)}/content", Content("stale", Token(note)));
        var stalePut = await client.PutAsJsonAsync($"/api/notes/{Id(note)}", Metadata("stale", Token(note)));

        Assert.Equal(HttpStatusCode.Conflict, stalePatch.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, stalePut.StatusCode);
        var stored = await GetAsync(client, Id(note));
        Assert.Equal("second", Text(stored));
        Assert.Equal(current, Token(stored));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task MissingToken_Returns400(string? token)
    {
        var client = await fixture.NewUserClientAsync();
        var note = await CreateAsync(client);

        var response = await client.PatchAsJsonAsync($"/api/notes/{Id(note)}/content", Content("x", token));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- concurrency ----

    [Fact]
    public async Task TwoContextsWithSameToken_SecondWriterIsRejected()
    {
        var client = await fixture.NewUserClientAsync();
        var note = await CreateAsync(client);
        var id = Guid.Parse(Id(note));

        using var scopeA = fixture.CreateScope();
        using var scopeB = fixture.CreateScope();
        var dbA = scopeA.ServiceProvider.GetRequiredService<AppDbContext>();
        var dbB = scopeB.ServiceProvider.GetRequiredService<AppDbContext>();
        var a = await dbA.Notes.FirstAsync(x => x.Id == id);
        var b = await dbB.Notes.FirstAsync(x => x.Id == id);

        a.VersionToken = "writer-a";
        b.VersionToken = "writer-b";
        await dbA.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => dbB.SaveChangesAsync());
    }

    [Fact]
    public async Task ParallelPatchesWithSameToken_ExactlyOneWins_OthersGet409()
    {
        var client = await fixture.NewUserClientAsync();
        var note = await CreateAsync(client);
        var id = Id(note);

        for (var round = 0; round < 10; round++)
        {
            var token = Token(await GetAsync(client, id));
            var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
                client.PatchAsJsonAsync($"/api/notes/{id}/content", Content($"r{round}-w{i}", token))));

            var statuses = responses.Select(r => r.StatusCode).ToList();
            Assert.Equal(1, statuses.Count(s => s == HttpStatusCode.OK));
            Assert.Equal(5, statuses.Count(s => s == HttpStatusCode.Conflict));

            var winner = await Json(responses.Single(r => r.StatusCode == HttpStatusCode.OK));
            var stored = await GetAsync(client, id);
            Assert.Equal(Text(winner), Text(stored));
            Assert.Equal(Token(winner), Token(stored));
        }
    }

    [Fact]
    public async Task OverlappingPatchAndPut_ExactlyOneWins()
    {
        var client = await fixture.NewUserClientAsync();
        var note = await CreateAsync(client);
        var id = Id(note);

        for (var round = 0; round < 10; round++)
        {
            var token = Token(await GetAsync(client, id));
            var responses = await Task.WhenAll(
                client.PatchAsJsonAsync($"/api/notes/{id}/content", Content("patch", token)),
                client.PutAsJsonAsync($"/api/notes/{id}", Metadata("put", token)));

            var statuses = responses.Select(r => r.StatusCode).OrderBy(s => (int)s).ToList();
            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], statuses);
        }
    }

    [Fact]
    public async Task DeleteRacingWithSaves_NeverReturns500()
    {
        var client = await fixture.NewUserClientAsync();

        for (var round = 0; round < 10; round++)
        {
            var note = await CreateAsync(client);
            var id = Id(note);
            var responses = await Task.WhenAll(
                client.PatchAsJsonAsync($"/api/notes/{id}/content", Content("patch", Token(note))),
                client.DeleteAsync($"/api/notes/{id}"));

            Assert.All(responses, r => Assert.True((int)r.StatusCode < 500, $"got {(int)r.StatusCode}"));
        }
    }

    // ---- content edge cases ----

    [Fact]
    public async Task LargeContent_RoundTripsWithoutTruncation()
    {
        var client = await fixture.NewUserClientAsync();
        var text = new string('ა', 200_000);

        var note = await CreateAsync(client, "x");
        var response = await client.PatchAsJsonAsync(
            $"/api/notes/{Id(note)}/content",
            new { contentJson = Doc(text), plainTextPreview = text[..50_000], versionToken = Token(note) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await GetAsync(client, Id(note));
        Assert.Equal(200_000, Text(stored).Length);
        Assert.Equal(50_000, stored.GetProperty("plainTextPreview").GetString()!.Length);
    }

    [Fact]
    public async Task TitleOverLimit_Returns400_NotSilentTruncation()
    {
        var client = await fixture.NewUserClientAsync();
        var title = Body("x");
        title["title"] = new string('t', 161);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/notes", title)).StatusCode);
    }

    [Fact]
    public async Task LongPreview_IsShortened_AndContentIsKeptWhole()
    {
        var client = await fixture.NewUserClientAsync();
        var text = new string('ა', 60_000);

        var response = await client.PostAsJsonAsync("/api/notes", Body(text));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var note = await Json(response);
        Assert.Equal(50_000, note.GetProperty("plainTextPreview").GetString()!.Length);
        Assert.Equal(60_000, Text(note).Length);
    }

    [Fact]
    public async Task LongPreview_NeverCutsThroughAnEmoji()
    {
        var client = await fixture.NewUserClientAsync();
        var body = Body("x");
        body["plainTextPreview"] = new string('p', 49_999) + "😀😀";

        var response = await client.PostAsJsonAsync("/api/notes", body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(49_999, (await Json(response)).GetProperty("plainTextPreview").GetString()!.Length);
    }

    private static object[] Paragraphs(int count) => Enumerable.Range(0, count)
        .Select(i => (object)new { type = "paragraph", children = new[] { new { textNode = new { type = "text", text = $"line {i}" } } } })
        .ToArray();

    [Theory]
    [InlineData(500)]
    [InlineData(501)]
    [InlineData(5000)]
    public async Task ManyBlocks_UpToLimit_AreSavedIntact(int count)
    {
        var client = await fixture.NewUserClientAsync();
        var body = Body("x");
        body["contentJson"] = new { version = 1, blocks = Paragraphs(count) };

        var response = await client.PostAsJsonAsync("/api/notes", body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var stored = await GetAsync(client, Id(await Json(response)));
        Assert.Equal(count, stored.GetProperty("contentJson").GetProperty("blocks").GetArrayLength());
    }

    [Fact]
    public async Task BlocksOverLimit_Return400()
    {
        var client = await fixture.NewUserClientAsync();
        var body = Body("x");
        body["contentJson"] = new { version = 1, blocks = Paragraphs(5001) };

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/notes", body)).StatusCode);
    }

    private static object CheckItem(string text, bool? isChecked, object? nested = null)
    {
        var child = new { textNode = new { type = "text", text } };
        return new
        {
            type = "checkboxListItem",
            @checked = isChecked,
            children = nested is null ? new object[] { child } : [child, new { blockNode = nested }]
        };
    }

    [Fact]
    public async Task CheckboxState_RoundTrips_ThroughPostPatchAndGet_IncludingNested()
    {
        var client = await fixture.NewUserClientAsync();
        var body = Body("x");
        body["contentJson"] = new { version = 1, blocks = new[] { CheckItem("outer", true, CheckItem("inner", true)) } };

        var created = await client.PostAsJsonAsync("/api/notes", body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var note = await Json(created);

        static void AssertChecked(JsonElement n, bool expected)
        {
            var outer = n.GetProperty("contentJson").GetProperty("blocks")[0];
            Assert.Equal(expected, outer.GetProperty("checked").GetBoolean());
            var inner = outer.GetProperty("children")[1].GetProperty("blockNode");
            Assert.Equal(expected, inner.GetProperty("checked").GetBoolean());
        }

        AssertChecked(note, true);
        AssertChecked(await GetAsync(client, Id(note)), true);

        var patched = await client.PatchAsJsonAsync($"/api/notes/{Id(note)}/content", new
        {
            contentJson = new { version = 1, blocks = new[] { CheckItem("outer", false, CheckItem("inner", false)) } },
            plainTextPreview = "x",
            versionToken = Token(note)
        });
        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
        AssertChecked(await Json(patched), false);
        AssertChecked(await GetAsync(client, Id(note)), false);
    }

    [Fact]
    public async Task BlocksWithoutCheckedField_StillWork_AndReadBackAsNull()
    {
        var client = await fixture.NewUserClientAsync();

        var note = await CreateAsync(client, "plain");

        var block = (await GetAsync(client, Id(note))).GetProperty("contentJson").GetProperty("blocks")[0];
        Assert.Equal(JsonValueKind.Null, block.GetProperty("checked").ValueKind);
    }

    [Fact]
    public async Task SixNestedListLevels_WithLinkMarks_RoundTrip()
    {
        var client = await fixture.NewUserClientAsync();
        object? nested = null;
        for (var level = 0; level < 6; level++)
        {
            var mark = new { type = "link", value = "https://example.com/a?b=1" };
            var child = new { textNode = new { type = "text", text = "item", marks = new[] { mark } } };
            var item = new { type = "listItem", children = nested is null ? new object[] { child } : [child, new { blockNode = nested }] };
            nested = new { type = "bulletList", children = new object[] { new { blockNode = item } } };
        }

        var body = Body("x");
        body["contentJson"] = new { version = 1, blocks = new[] { nested } };
        var payload = JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web) { MaxDepth = 256 });

        var response = await client.PostAsync("/api/notes", new StringContent(payload, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var stored = await client.GetStringAsync($"/api/notes/{Id(await Json(response))}");
        Assert.Contains("https://example.com/a?b=1", stored);
    }

    [Fact]
    public async Task ClearingAllContent_ByPatch_IsAllowed()
    {
        var client = await fixture.NewUserClientAsync();
        var note = await CreateAsync(client);

        var response = await client.PatchAsJsonAsync(
            $"/api/notes/{Id(note)}/content",
            new { contentJson = new { version = 1, blocks = Array.Empty<object>() }, plainTextPreview = "", versionToken = Token(note) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreatingCompletelyEmptyNote_Returns400()
    {
        var client = await fixture.NewUserClientAsync();
        var body = Body("x");
        body["title"] = "";
        body["plainTextPreview"] = "";
        body["contentJson"] = new { version = 1, blocks = Array.Empty<object>() };

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/notes", body)).StatusCode);
    }

    [Fact]
    public async Task NulCharacter_IsStrippedAndSaved_Not500()
    {
        var client = await fixture.NewUserClientAsync();
        var body = Body("dec\u0000ember");
        body["title"] = "ti\u0000tle";

        var response = await client.PostAsJsonAsync("/api/notes", body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var note = await Json(response);
        Assert.Equal("december", Text(note));
        Assert.Equal("december", note.GetProperty("plainTextPreview").GetString());
        Assert.Equal("title", note.GetProperty("title").GetString());
    }

    private static async Task<HttpResponseMessage> PostNestedAsync(HttpClient client, int levels)
    {
        object node = new { textNode = new { type = "text", text = "deep" } };
        for (var i = 0; i < levels; i++)
        {
            node = new { blockNode = new { type = "list", children = new[] { node } } };
        }

        var body = Body("x");
        body["contentJson"] = new { version = 1, blocks = new[] { new { type = "paragraph", children = new[] { node } } } };
        var payload = JsonSerializer.Serialize(body, new JsonSerializerOptions { MaxDepth = 512 });
        return await client.PostAsync("/api/notes", new StringContent(payload, System.Text.Encoding.UTF8, "application/json"));
    }

    [Fact]
    public async Task DeeplyNestedContent_WithinJsonLimit_IsSavedIntact()
    {
        var client = await fixture.NewUserClientAsync();

        var response = await PostNestedAsync(client, levels: 18);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var stored = await GetAsync(client, Id(await Json(response)));
        var node = stored.GetProperty("contentJson").GetProperty("blocks")[0].GetProperty("children")[0];
        for (var i = 0; i < 18; i++)
        {
            node = node.GetProperty("blockNode").GetProperty("children")[0];
        }

        Assert.Equal("deep", node.GetProperty("textNode").GetProperty("text").GetString());
    }

    [Fact]
    public async Task ContentBeyondJsonDepthLimit_Returns400_AndStoresNothing()
    {
        var client = await fixture.NewUserClientAsync();

        var response = await PostNestedAsync(client, levels: 40);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>("/api/notes")).GetArrayLength());
    }

    [Theory]
    [InlineData("""{"version":2,"blocks":[]}""")]
    [InlineData("""{"version":1}""")]
    [InlineData("""{"version":1,"blocks":"nope"}""")]
    [InlineData("\"not an object\"")]
    [InlineData("null")]
    public async Task InvalidContentJson_Returns400(string contentJson)
    {
        var client = await fixture.NewUserClientAsync();
        var payload = $$"""
            {"title":"t","contentJson":{{contentJson}},"plainTextPreview":"p","fontFamily":"system-ui","fontSize":16,"fontWeight":400,"isPinned":false,"isArchived":false}
            """;

        var response = await client.PostAsync("/api/notes", new StringContent(payload, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OmittedOptionalFields_FallBackToDefaults()
    {
        var client = await fixture.NewUserClientAsync();
        var body = Body("x");
        body.Remove("fontFamily");
        body.Remove("textColor");
        body.Remove("backgroundColor");

        var response = await client.PostAsJsonAsync("/api/notes", body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("system-ui", (await Json(response)).GetProperty("fontFamily").GetString());
    }

    [Fact]
    public async Task NoteStoredInMigratedCamelCaseShape_StillReadsItsContent()
    {
        var client = await fixture.NewUserClientAsync();
        var note = await CreateAsync(client);
        var id = Guid.Parse(Id(note));
        // Exact shape written by migration UpgradeNotesToRichText for pre-rich-text notes.
        const string legacy = """{"version":1,"blocks":[{"type":"paragraph","align":"left","children":[{"textNode":{"type":"text","text":"legacy text"}}]}]}""";
        await fixture.WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE notes SET "ContentJson" = {legacy}::jsonb WHERE "Id" = {id}"""));

        var stored = await GetAsync(client, Id(note));

        Assert.Equal(1, stored.GetProperty("contentJson").GetProperty("version").GetInt32());
        Assert.Equal("legacy text", Text(stored));
    }

    // ---- metadata-only PUT, as sent by installed app builds ----

    private static Dictionary<string, object?> MetadataOnly(string token, string title = "Renamed")
    {
        var body = Metadata("unused", token);
        body["title"] = title;
        body.Remove("contentJson");
        body.Remove("plainTextPreview");
        return body;
    }

    [Fact]
    public async Task PutWithoutContent_KeepsStoredContent_AndUpdatesMetadata()
    {
        var client = await fixture.NewUserClientAsync();
        var note = await CreateAsync(client, "keep me");

        var response = await client.PutAsJsonAsync($"/api/notes/{Id(note)}", MetadataOnly(Token(note)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await Json(response);
        Assert.Equal("Renamed", updated.GetProperty("title").GetString());
        Assert.NotEqual(Token(note), Token(updated));
        var stored = await GetAsync(client, Id(note));
        Assert.Equal("keep me", Text(stored));
        Assert.Equal("keep me", stored.GetProperty("plainTextPreview").GetString());
    }

    [Fact]
    public async Task PutWithoutContent_StaleToken_Returns409()
    {
        var client = await fixture.NewUserClientAsync();
        var note = await CreateAsync(client);
        await client.PatchAsJsonAsync($"/api/notes/{Id(note)}/content", Content("newer", Token(note)));

        var response = await client.PutAsJsonAsync($"/api/notes/{Id(note)}", MetadataOnly(Token(note)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PutWithoutContent_BlankingTheLastContent_Returns400()
    {
        var client = await fixture.NewUserClientAsync();
        var body = Body("x");
        body["plainTextPreview"] = "";
        body["contentJson"] = new { version = 1, blocks = Array.Empty<object>() };
        var created = await client.PostAsJsonAsync("/api/notes", body);
        var note = await Json(created);

        var response = await client.PutAsJsonAsync($"/api/notes/{Id(note)}", MetadataOnly(Token(note), title: ""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutWithPreviewButNoContent_Returns400_RatherThanDesyncingThem()
    {
        var client = await fixture.NewUserClientAsync();
        var note = await CreateAsync(client);
        var body = MetadataOnly(Token(note));
        body["plainTextPreview"] = "preview without content";

        var response = await client.PutAsJsonAsync($"/api/notes/{Id(note)}", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- authorization ----

    [Fact]
    public async Task AnotherUsersNote_IsInvisibleAndUntouchable()
    {
        var owner = await fixture.NewUserClientAsync();
        var other = await fixture.NewUserClientAsync();
        var note = await CreateAsync(owner, "private");
        var id = Id(note);
        var token = Token(note);

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/notes/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PatchAsJsonAsync($"/api/notes/{id}/content", Content("hacked", token))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/api/notes/{id}", Metadata("hacked", token))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/notes/{id}/duplicate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/notes/{id}")).StatusCode);
        Assert.Equal(0, (await other.GetFromJsonAsync<JsonElement>("/api/notes")).GetArrayLength());

        var stored = await GetAsync(owner, id);
        Assert.Equal("private", Text(stored));
        Assert.Equal(token, Token(stored));
    }

    [Fact]
    public async Task WithoutToken_EveryRouteReturns401()
    {
        var client = fixture.AnonymousClient();
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/notes")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/notes", Body())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PatchAsJsonAsync($"/api/notes/{id}/content", Content("x", "t"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync($"/api/notes/{id}", Metadata("x", "t"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync($"/api/notes/{id}")).StatusCode);
    }

    // ---- deleted / archived ----

    [Fact]
    public async Task DeletedNote_Returns404Everywhere_AndIsSoftDeleted()
    {
        var client = await fixture.NewUserClientAsync();
        var note = await CreateAsync(client);
        var id = Id(note);
        var token = Token(note);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/notes/{id}")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/notes/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PatchAsJsonAsync($"/api/notes/{id}/content", Content("x", token))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/notes/{id}", Metadata("x", token))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/notes/{id}")).StatusCode);

        Note? raw = null;
        await fixture.WithDbAsync(async db => raw = await db.Notes.IgnoreQueryFilters().FirstAsync(x => x.Id == Guid.Parse(id)));
        Assert.True(raw!.IsDeleted);
        Assert.NotNull(raw.DeletedAtUtc);
    }

    [Fact]
    public async Task ArchivedNote_IsHiddenByDefault_ButStillEditable()
    {
        var client = await fixture.NewUserClientAsync();
        var note = await CreateAsync(client, "archived text");
        var id = Id(note);
        var body = Metadata("archived text", Token(note));
        body["isArchived"] = true;
        var archive = await client.PutAsJsonAsync($"/api/notes/{id}", body);
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);

        Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>("/api/notes")).GetArrayLength());
        Assert.Equal(1, (await client.GetFromJsonAsync<JsonElement>("/api/notes?includeArchived=true")).GetArrayLength());

        var edit = await client.PatchAsJsonAsync($"/api/notes/{id}/content", Content("edited", Token(await Json(archive))));
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
    }
}
