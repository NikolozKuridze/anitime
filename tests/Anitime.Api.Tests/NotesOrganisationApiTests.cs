using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Anitime.Application.Abstractions;
using Anitime.Infrastructure.Notes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Anitime.Api.Tests;

/// <summary>Notes P2/P3: folders, tags, smart folders, Recently Deleted, edit-clash copies, attachments, search, backlinks.</summary>
[Collection(ApiCollection.Name)]
public sealed class NotesOrganisationApiTests(ApiFixture fixture)
{
    // ---- helpers ----

    private const string Metadata =
        "\"fontFamily\":\"system-ui\",\"fontSize\":16,\"fontWeight\":400,\"textColor\":\"#1F2937\",\"backgroundColor\":\"#FFFFFF\",\"isPinned\":false,\"isArchived\":false";

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52];

    private static StringContent Raw(string json) => new(json, Encoding.UTF8, "application/json");
    private static Task<JsonElement> Json(HttpResponseMessage response) => response.Content.ReadFromJsonAsync<JsonElement>();
    private static string Id(JsonElement element) => element.GetProperty("id").GetString()!;
    private static string Token(JsonElement note) => note.GetProperty("versionToken").GetString()!;
    private static string Str(string value) => JsonSerializer.Serialize(value);

    private static string Text(string text, string? marks = null) =>
        $"{{\"type\":\"text\",\"text\":{Str(text)}{(marks is null ? "" : $",\"marks\":{marks}")}}}";

    private static string Paragraph(params string[] inline) => $"{{\"type\":\"paragraph\",\"content\":[{string.Join(",", inline)}]}}";
    private static string V2(params string[] blocks) => $"{{\"version\":2,\"content\":[{string.Join(",", blocks)}]}}";
    private static string Checklist(string text) =>
        $"{{\"type\":\"taskList\",\"content\":[{{\"type\":\"taskItem\",\"attrs\":{{\"checked\":false}},\"content\":[{Paragraph(Text(text))}]}}]}}";
    private static string Image(string assetId) => $"{{\"type\":\"image\",\"attrs\":{{\"assetId\":\"{assetId}\"}}}}";
    private static string NoteLink(string text, string noteId) => Text(text, $"[{{\"type\":\"link\",\"attrs\":{{\"href\":\"anitime-note:{noteId}\"}}}}]");

    private static string CreateBody(string title, string contentJson, string? extra = null) =>
        $"{{\"title\":{Str(title)},{Metadata},\"contentJson\":{contentJson}{(extra is null ? "" : "," + extra)}}}";

    private static async Task<JsonElement> CreateNoteAsync(HttpClient client, string title, string? contentJson = null, string? extra = null)
    {
        var response = await client.PostAsync("/api/notes", Raw(CreateBody(title, contentJson ?? V2(Paragraph(Text(title))), extra)));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await Json(response);
    }

    private static async Task<JsonElement> CreateFolderAsync(HttpClient client, string name, string? parentId = null)
    {
        var body = parentId is null ? $"{{\"name\":{Str(name)}}}" : $"{{\"name\":{Str(name)},\"parentId\":\"{parentId}\"}}";
        var response = await client.PostAsync("/api/notes/folders", Raw(body));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await Json(response);
    }

    private static async Task<string[]> ListTitlesAsync(HttpClient client, string query = "")
    {
        var list = await client.GetFromJsonAsync<JsonElement>($"/api/notes{query}");
        return list.EnumerateArray().Select(n => n.GetProperty("title").GetString()!).Order(StringComparer.Ordinal).ToArray();
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"{response.StatusCode}: {body}");
        Assert.Equal(code, JsonDocument.Parse(body).RootElement.GetProperty("code").GetString());
    }

    private static MultipartFormDataContent Upload(byte[] bytes, string contentType = "image/png")
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { content, "file", "photo.png" } };
    }

    private static async Task<string> UploadAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/notes/attachments", Upload(Png));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await Json(response)).GetProperty("assetId").GetString()!;
    }

    // ---- folders ----

    [Fact]
    public async Task Folders_CreateNestOneLevel_ListWithCounts_Rename()
    {
        var client = await fixture.NewUserClientAsync();
        var work = await CreateFolderAsync(client, "Work");
        var projects = await CreateFolderAsync(client, "Projects", Id(work));
        await CreateNoteAsync(client, "a", extra: $"\"folderId\":\"{Id(work)}\"");
        await CreateNoteAsync(client, "b", extra: $"\"folderId\":\"{Id(projects)}\"");
        await CreateNoteAsync(client, "c", extra: $"\"folderId\":\"{Id(projects)}\"");

        var tooDeep = await client.PostAsync("/api/notes/folders", Raw($"{{\"name\":\"x\",\"parentId\":\"{Id(projects)}\"}}"));
        var renamed = await client.PutAsync($"/api/notes/folders/{Id(work)}", Raw("{\"name\":\"Office\"}"));
        var folders = await client.GetFromJsonAsync<JsonElement>("/api/notes/folders");

        await AssertErrorAsync(tooDeep, HttpStatusCode.BadRequest, "validation_failed");
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var byName = folders.EnumerateArray().ToDictionary(f => f.GetProperty("name").GetString()!);
        Assert.Equal(1, byName["Office"].GetProperty("noteCount").GetInt32());
        Assert.Equal(2, byName["Projects"].GetProperty("noteCount").GetInt32());
        Assert.Equal(Id(work), byName["Projects"].GetProperty("parentId").GetString());
        Assert.Equal(JsonValueKind.Null, byName["Office"].GetProperty("parentId").ValueKind);
    }

    [Fact]
    public async Task Folders_DepthStaysAtTwo_WhenMovingFolders()
    {
        var client = await fixture.NewUserClientAsync();
        var a = await CreateFolderAsync(client, "A");
        var b = await CreateFolderAsync(client, "B");
        await CreateFolderAsync(client, "B child", Id(b));

        var parentWithChildren = await client.PutAsync($"/api/notes/folders/{Id(b)}", Raw($"{{\"name\":\"B\",\"parentId\":\"{Id(a)}\"}}"));
        var self = await client.PutAsync($"/api/notes/folders/{Id(a)}", Raw($"{{\"name\":\"A\",\"parentId\":\"{Id(a)}\"}}"));

        await AssertErrorAsync(parentWithChildren, HttpStatusCode.BadRequest, "validation_failed");
        await AssertErrorAsync(self, HttpStatusCode.BadRequest, "validation_failed");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Folders_BlankName_Returns400(string name)
    {
        var client = await fixture.NewUserClientAsync();

        var response = await client.PostAsync("/api/notes/folders", Raw($"{{\"name\":{Str(name)}}}"));

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Folders_AreIsolatedPerUser()
    {
        var owner = await fixture.NewUserClientAsync();
        var other = await fixture.NewUserClientAsync();
        var folder = await CreateFolderAsync(owner, "Private");

        Assert.Equal(0, (await other.GetFromJsonAsync<JsonElement>("/api/notes/folders")).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsync($"/api/notes/folders/{Id(folder)}", Raw("{\"name\":\"x\"}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/notes/folders/{Id(folder)}")).StatusCode);
        await AssertErrorAsync(await other.PostAsync("/api/notes", Raw(CreateBody("n", V2(Paragraph(Text("n"))), $"\"folderId\":\"{Id(folder)}\""))),
            HttpStatusCode.BadRequest, "validation_failed");
        await AssertErrorAsync(await other.PostAsync("/api/notes/folders", Raw($"{{\"name\":\"x\",\"parentId\":\"{Id(folder)}\"}}")),
            HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task MoveNotes_IntoFolderAndBackOut_AndFilterByFolder()
    {
        var client = await fixture.NewUserClientAsync();
        var folder = await CreateFolderAsync(client, "F");
        var a = await CreateNoteAsync(client, "a");
        var b = await CreateNoteAsync(client, "b");
        await CreateNoteAsync(client, "c");

        var moved = await client.PostAsync("/api/notes/move", Raw($"{{\"noteIds\":[\"{Id(a)}\",\"{Id(b)}\"],\"folderId\":\"{Id(folder)}\"}}"));
        Assert.Equal(2, (await Json(moved)).GetProperty("moved").GetInt32());
        Assert.Equal(["a", "b"], await ListTitlesAsync(client, $"?folderId={Id(folder)}"));

        await client.PostAsync("/api/notes/move", Raw($"{{\"noteIds\":[\"{Id(a)}\"],\"folderId\":null}}"));
        Assert.Equal(["b"], await ListTitlesAsync(client, $"?folderId={Id(folder)}"));
        Assert.Equal(["a", "c"], await ListTitlesAsync(client, "?folderId=none"));
    }

    [Fact]
    public async Task MoveNotes_DoesNotRotateTheVersionToken()
    {
        var client = await fixture.NewUserClientAsync();
        var folder = await CreateFolderAsync(client, "F");
        var note = await CreateNoteAsync(client, "a");

        await client.PostAsync("/api/notes/move", Raw($"{{\"noteIds\":[\"{Id(note)}\"],\"folderId\":\"{Id(folder)}\"}}"));

        var stored = await client.GetFromJsonAsync<JsonElement>($"/api/notes/{Id(note)}");
        Assert.Equal(Token(note), Token(stored));
        Assert.Equal(Id(folder), stored.GetProperty("folderId").GetString());
    }

    [Fact]
    public async Task MoveNotes_OtherUsersNotesAndFolders_AreRejected()
    {
        var owner = await fixture.NewUserClientAsync();
        var other = await fixture.NewUserClientAsync();
        var note = await CreateNoteAsync(owner, "mine");
        var otherFolder = await CreateFolderAsync(other, "theirs");
        var ownFolder = await CreateFolderAsync(owner, "own");

        var foreignFolder = await owner.PostAsync("/api/notes/move", Raw($"{{\"noteIds\":[\"{Id(note)}\"],\"folderId\":\"{Id(otherFolder)}\"}}"));
        var foreignNote = await other.PostAsync("/api/notes/move", Raw($"{{\"noteIds\":[\"{Id(note)}\"],\"folderId\":\"{Id(otherFolder)}\"}}"));

        await AssertErrorAsync(foreignFolder, HttpStatusCode.BadRequest, "validation_failed");
        Assert.Equal(0, (await Json(foreignNote)).GetProperty("moved").GetInt32());
        Assert.Equal(["mine"], await ListTitlesAsync(owner, "?folderId=none"));
        Assert.Empty(await ListTitlesAsync(owner, $"?folderId={Id(ownFolder)}"));
    }

    [Fact]
    public async Task DeleteFolder_KeepsNotesUnfiled_ByDefault_AndPromotesChildren()
    {
        var client = await fixture.NewUserClientAsync();
        var parent = await CreateFolderAsync(client, "Parent");
        var child = await CreateFolderAsync(client, "Child", Id(parent));
        await CreateNoteAsync(client, "kept", extra: $"\"folderId\":\"{Id(parent)}\"");

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/notes/folders/{Id(parent)}")).StatusCode);

        Assert.Equal(["kept"], await ListTitlesAsync(client, "?folderId=none"));
        var folders = await client.GetFromJsonAsync<JsonElement>("/api/notes/folders");
        var remaining = Assert.Single(folders.EnumerateArray());
        Assert.Equal(Id(child), Id(remaining));
        Assert.Equal(JsonValueKind.Null, remaining.GetProperty("parentId").ValueKind);
    }

    [Fact]
    public async Task DeleteFolder_WithDeleteNotes_SendsItsNotesToRecentlyDeleted()
    {
        var client = await fixture.NewUserClientAsync();
        var folder = await CreateFolderAsync(client, "Old");
        await CreateNoteAsync(client, "gone", extra: $"\"folderId\":\"{Id(folder)}\"");
        await CreateNoteAsync(client, "stays");

        await client.DeleteAsync($"/api/notes/folders/{Id(folder)}?deleteNotes=true");

        Assert.Equal(["stays"], await ListTitlesAsync(client));
        var deleted = await client.GetFromJsonAsync<JsonElement>("/api/notes/deleted");
        Assert.Equal("gone", Assert.Single(deleted.EnumerateArray()).GetProperty("title").GetString());
    }

    // ---- metadata: lock, paper, colours, and installed apps ----

    [Fact]
    public async Task LockPaperAndPaletteColours_AreStored()
    {
        var client = await fixture.NewUserClientAsync();

        var note = await CreateNoteAsync(client, "styled", extra: "\"isLocked\":true,\"paperStyle\":\"grid\"");
        var put = await client.PutAsync($"/api/notes/{Id(note)}",
            Raw($"{{\"title\":\"styled\",\"fontFamily\":\"system-ui\",\"fontSize\":16,\"fontWeight\":400,\"textColor\":\"purple\",\"backgroundColor\":\"mint\",\"isPinned\":false,\"isArchived\":false,\"paperStyle\":\"lines\",\"versionToken\":\"{Token(note)}\"}}"));

        Assert.True(note.GetProperty("isLocked").GetBoolean());
        Assert.Equal("grid", note.GetProperty("paperStyle").GetString());
        var updated = await Json(put);
        Assert.Equal("lines", updated.GetProperty("paperStyle").GetString());
        Assert.True(updated.GetProperty("isLocked").GetBoolean());
        Assert.Equal("mint", updated.GetProperty("backgroundColor").GetString());
    }

    [Theory]
    [InlineData("\"paperStyle\":\"dots\"")]
    [InlineData("\"backgroundColor\":\"red\"")]
    public async Task InvalidPaperOrColour_Returns400(string field)
    {
        var client = await fixture.NewUserClientAsync();
        var body = CreateBody("x", V2(Paragraph(Text("x"))));
        body = field.StartsWith("\"backgroundColor\"") ? body.Replace("\"backgroundColor\":\"#FFFFFF\"", field) : body[..^1] + "," + field + "}";

        await AssertErrorAsync(await client.PostAsync("/api/notes", Raw(body)), HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task InstalledAppPut_WithoutNewFields_KeepsFolderLockPaperAndTags()
    {
        var client = await fixture.NewUserClientAsync();
        var folder = await CreateFolderAsync(client, "F");
        var v1 = "{\"version\":1,\"blocks\":[{\"type\":\"paragraph\",\"align\":\"left\",\"children\":[{\"textNode\":{\"type\":\"text\",\"text\":\"n\"}}]}]}";
        var note = await CreateNoteAsync(client, "n", v1, $"\"plainTextPreview\":\"n\",\"folderId\":\"{Id(folder)}\",\"isLocked\":true,\"paperStyle\":\"grid\",\"tags\":[\"keep\"]");

        // Exactly what today's App.tsx sends: metadata + v1 content + token, none of the new fields.
        var put = await client.PutAsync($"/api/notes/{Id(note)}",
            Raw($"{{\"title\":\"n\",{Metadata},\"contentJson\":{v1},\"plainTextPreview\":\"n\",\"versionToken\":\"{Token(note)}\"}}"));
        var stored = await Json(put);

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal(Id(folder), stored.GetProperty("folderId").GetString());
        Assert.True(stored.GetProperty("isLocked").GetBoolean());
        Assert.Equal("grid", stored.GetProperty("paperStyle").GetString());
        Assert.Equal(["keep"], stored.GetProperty("explicitTags").EnumerateArray().Select(t => t.GetString()));
    }

    // ---- tags ----

    [Fact]
    public async Task Tags_AreParsedFromV2Text_AndMergedWithExplicitTags()
    {
        var client = await fixture.NewUserClientAsync();
        var content = V2(
            Paragraph(Text("Plan for #Work and #ქართული, not a#b or #123 ")),
            Paragraph(Text("#codeMark", "[{\"type\":\"code\"}]"), Text(" "), Text("#linked", "[{\"type\":\"link\",\"attrs\":{\"href\":\"https://x.y\"}}]")),
            "{\"type\":\"codeBlock\",\"content\":[{\"type\":\"text\",\"text\":\"#codeblock\"}]}",
            Checklist("buy milk #shopping-list"));

        var note = await CreateNoteAsync(client, "tagged", content, "\"tags\":[\"#Ideas\",\"plans\"]");

        Assert.Equal(["ideas", "plans", "shopping-list", "work", "ქართული"], note.GetProperty("tags").EnumerateArray().Select(t => t.GetString()));
        Assert.Equal(["ideas", "plans"], note.GetProperty("explicitTags").EnumerateArray().Select(t => t.GetString()));
    }

    [Theory]
    [InlineData("\"two words\"")]
    [InlineData("\"\"")]
    [InlineData("\"###\"")]
    public async Task InvalidExplicitTag_Returns400(string tag)
    {
        var client = await fixture.NewUserClientAsync();

        var response = await client.PostAsync("/api/notes", Raw(CreateBody("x", V2(Paragraph(Text("x"))), $"\"tags\":[{tag}]")));

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task TagList_HasCounts_FilterWorks_AndDeletedNotesDontCount()
    {
        var client = await fixture.NewUserClientAsync();
        await CreateNoteAsync(client, "one", V2(Paragraph(Text("#work #home"))));
        await CreateNoteAsync(client, "two", V2(Paragraph(Text("#work"))));
        var gone = await CreateNoteAsync(client, "three", V2(Paragraph(Text("#work #trash"))));
        await client.DeleteAsync($"/api/notes/{Id(gone)}");

        var tags = await client.GetFromJsonAsync<JsonElement>("/api/notes/tags");

        Assert.Equal(["home:1", "work:2"], tags.EnumerateArray().Select(t => $"{t.GetProperty("name").GetString()}:{t.GetProperty("noteCount").GetInt32()}"));
        Assert.Equal(["one", "two"], await ListTitlesAsync(client, "?tag=work"));
        Assert.Equal(["one"], await ListTitlesAsync(client, "?tag=%23Home"));
    }

    [Fact]
    public async Task MetadataOnlyPut_WithTags_KeepsParsedTags()
    {
        var client = await fixture.NewUserClientAsync();
        var note = await CreateNoteAsync(client, "n", V2(Paragraph(Text("#parsed"))));

        var put = await client.PutAsync($"/api/notes/{Id(note)}",
            Raw($"{{\"title\":\"n\",{Metadata},\"tags\":[\"added\"],\"versionToken\":\"{Token(note)}\"}}"));

        Assert.Equal(["added", "parsed"], (await Json(put)).GetProperty("tags").EnumerateArray().Select(t => t.GetString()));
    }

    // ---- smart folders ----

    [Fact]
    public async Task SmartFolders_AreEvaluatedOnTheServer()
    {
        var client = await fixture.NewUserClientAsync();
        var assetId = await UploadAsync(client);
        await CreateNoteAsync(client, "checklist", V2(Checklist("milk")));
        await CreateNoteAsync(client, "v1 checklist", "{\"version\":1,\"blocks\":[{\"type\":\"checkboxListItem\",\"align\":\"left\",\"checked\":false,\"children\":[{\"textNode\":{\"type\":\"text\",\"text\":\"x\"}}]}]}", "\"plainTextPreview\":\"x\"");
        await CreateNoteAsync(client, "image", V2(Image(assetId)));
        await CreateNoteAsync(client, "work home", V2(Paragraph(Text("#work #home"))));
        await CreateNoteAsync(client, "work", V2(Paragraph(Text("#work"))), "\"isLocked\":true");
        var pinned = await CreateNoteAsync(client, "pinned");
        await client.PutAsync($"/api/notes/{Id(pinned)}", Raw($"{{\"title\":\"pinned\",\"fontFamily\":\"system-ui\",\"fontSize\":16,\"fontWeight\":400,\"isPinned\":true,\"isArchived\":false,\"versionToken\":\"{Token(pinned)}\"}}"));

        async Task<string[]> Evaluate(string filter)
        {
            var created = await client.PostAsync("/api/notes/smart-folders", Raw($"{{\"name\":\"s\",\"filter\":{filter}}}"));
            Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
            return await ListTitlesAsync(client, $"?smartFolderId={Id(await Json(created))}");
        }

        Assert.Equal(["checklist", "v1 checklist"], await Evaluate("{\"hasChecklist\":true}"));
        Assert.Equal(["image"], await Evaluate("{\"hasImage\":true}"));
        Assert.Equal(["work", "work home"], await Evaluate("{\"tags\":[\"work\"]}"));
        Assert.Equal(["work home"], await Evaluate("{\"tags\":[\"work\",\"home\"],\"tagMode\":\"all\"}"));
        Assert.Equal(["work"], await Evaluate("{\"tags\":[\"work\"],\"isLocked\":true}"));
        Assert.Equal(["pinned"], await Evaluate("{\"isPinned\":true}"));
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        Assert.Equal(6, (await Evaluate($"{{\"createdFrom\":\"{today}\",\"editedTo\":\"{today}\"}}")).Length);
        Assert.Empty(await Evaluate("{\"createdTo\":\"2020-01-01\"}"));
    }

    [Fact]
    public async Task SmartFolders_CrudAndValidation()
    {
        var client = await fixture.NewUserClientAsync();
        var other = await fixture.NewUserClientAsync();

        var created = await Json(await client.PostAsync("/api/notes/smart-folders", Raw("{\"name\":\"Work\",\"filter\":{\"tags\":[\"#Work\"]}}")));
        var updated = await client.PutAsync($"/api/notes/smart-folders/{Id(created)}", Raw("{\"name\":\"Work stuff\",\"filter\":{\"tags\":[\"work\"],\"hasImage\":true}}"));
        var list = await client.GetFromJsonAsync<JsonElement>("/api/notes/smart-folders");

        Assert.Equal("work", created.GetProperty("filter").GetProperty("tags")[0].GetString());
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.True(Assert.Single(list.EnumerateArray()).GetProperty("filter").GetProperty("hasImage").GetBoolean());
        await AssertErrorAsync(await client.PostAsync("/api/notes/smart-folders", Raw("{\"name\":\"x\",\"filter\":{\"color\":\"red\"}}")), HttpStatusCode.BadRequest, "invalid_request");
        await AssertErrorAsync(await client.PostAsync("/api/notes/smart-folders", Raw("{\"name\":\"x\",\"filter\":{\"tagMode\":\"some\"}}")), HttpStatusCode.BadRequest, "validation_failed");
        await AssertErrorAsync(await client.PostAsync("/api/notes/smart-folders", Raw("{\"name\":\"x\",\"filter\":{\"createdFrom\":\"2026-02-01\",\"createdTo\":\"2026-01-01\"}}")), HttpStatusCode.BadRequest, "validation_failed");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/notes?smartFolderId={Id(created)}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/notes/smart-folders/{Id(created)}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/notes/smart-folders/{Id(created)}")).StatusCode);
    }

    // ---- Recently Deleted ----

    [Fact]
    public async Task RecentlyDeleted_ListRestoreAndDeleteForever()
    {
        var client = await fixture.NewUserClientAsync();
        var folder = await CreateFolderAsync(client, "F");
        var restored = await CreateNoteAsync(client, "restore me", extra: $"\"folderId\":\"{Id(folder)}\"");
        var forever = await CreateNoteAsync(client, "forget me");
        await client.DeleteAsync($"/api/notes/{Id(restored)}");
        await client.DeleteAsync($"/api/notes/{Id(forever)}");
        await client.DeleteAsync($"/api/notes/folders/{Id(folder)}");

        var deleted = await client.GetFromJsonAsync<JsonElement>("/api/notes/deleted");
        Assert.Equal(2, deleted.GetArrayLength());
        var row = deleted.EnumerateArray().First(n => n.GetProperty("title").GetString() == "restore me");
        var deletedAt = row.GetProperty("deletedAtUtc").GetDateTime();
        Assert.Equal(deletedAt.AddDays(30), row.GetProperty("purgeAtUtc").GetDateTime());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/notes/deleted/{Id(restored)}")).StatusCode);

        var restore = await client.PostAsync($"/api/notes/{Id(restored)}/restore", null);
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await Json(restore)).GetProperty("folderId").ValueKind); // its folder was deleted
        Assert.Equal(["restore me"], await ListTitlesAsync(client));

        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/notes/{Id(restored)}/forever")).StatusCode); // active notes need a normal delete first
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/notes/{Id(forever)}/forever")).StatusCode);
        Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>("/api/notes/deleted")).GetArrayLength());
        await fixture.WithDbAsync(async db =>
            Assert.False(await db.Notes.IgnoreQueryFilters().AnyAsync(n => n.Id == Guid.Parse(Id(forever)))));
    }

    [Fact]
    public async Task RecentlyDeleted_IsPerUser()
    {
        var owner = await fixture.NewUserClientAsync();
        var other = await fixture.NewUserClientAsync();
        var note = await CreateNoteAsync(owner, "x");
        await owner.DeleteAsync($"/api/notes/{Id(note)}");

        Assert.Equal(0, (await other.GetFromJsonAsync<JsonElement>("/api/notes/deleted")).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/notes/{Id(note)}/restore", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/notes/{Id(note)}/forever")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/notes/deleted/{Id(note)}")).StatusCode);
    }

    [Fact]
    public async Task PurgeJob_RemovesNotesDeletedMoreThan30DaysAgo()
    {
        var client = await fixture.NewUserClientAsync();
        var old = await CreateNoteAsync(client, "old");
        var recent = await CreateNoteAsync(client, "recent");
        await client.DeleteAsync($"/api/notes/{Id(old)}");
        await client.DeleteAsync($"/api/notes/{Id(recent)}");
        var oldId = Guid.Parse(Id(old));
        await fixture.WithDbAsync(db => db.Notes.IgnoreQueryFilters().Where(n => n.Id == oldId)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.DeletedAtUtc, DateTime.UtcNow.AddDays(-31))));

        await fixture.WithDbAsync(db => NotesMaintenance.PurgeDeletedNotesAsync(db, DateTime.UtcNow, CancellationToken.None));

        var deleted = await client.GetFromJsonAsync<JsonElement>("/api/notes/deleted");
        Assert.Equal("recent", Assert.Single(deleted.EnumerateArray()).GetProperty("title").GetString());
    }

    // ---- edit-clash copy ----

    [Fact]
    public async Task ConflictCopy_GoesStraightToRecentlyDeleted()
    {
        var client = await fixture.NewUserClientAsync();
        var note = await CreateNoteAsync(client, "Plan");

        var response = await client.PostAsync($"/api/notes/{Id(note)}/conflict-copy", Raw($"{{\"contentJson\":{V2(Paragraph(Text("the version not chosen")))}}}"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var copy = await Json(response);
        Assert.Equal("Copy of Plan", copy.GetProperty("title").GetString());
        Assert.Equal("the version not chosen", copy.GetProperty("plainTextPreview").GetString());
        Assert.Equal(["Plan"], await ListTitlesAsync(client));
        var deleted = await client.GetFromJsonAsync<JsonElement>("/api/notes/deleted");
        Assert.Equal(Id(copy), Id(Assert.Single(deleted.EnumerateArray())));
        // The original is untouched, so the user's chosen save still works with its token.
        Assert.Equal(Token(note), Token(await client.GetFromJsonAsync<JsonElement>($"/api/notes/{Id(note)}")));
    }

    [Fact]
    public async Task ConflictCopy_UsesTheGivenTitle_CapsLength_AndValidates()
    {
        var client = await fixture.NewUserClientAsync();
        var other = await fixture.NewUserClientAsync();
        var note = await CreateNoteAsync(client, "Plan");

        var titled = await Json(await client.PostAsync($"/api/notes/{Id(note)}/conflict-copy",
            Raw($"{{\"title\":{Str(new string('t', 160))},\"contentJson\":{V2(Paragraph(Text("x")))}}}")));
        var unknownField = await client.PostAsync($"/api/notes/{Id(note)}/conflict-copy", Raw($"{{\"contentJson\":{V2()},\"extra\":1}}"));
        var badContent = await client.PostAsync($"/api/notes/{Id(note)}/conflict-copy", Raw("{\"contentJson\":{\"version\":2,\"content\":[{\"type\":\"video\"}]}}"));
        var foreign = await other.PostAsync($"/api/notes/{Id(note)}/conflict-copy", Raw($"{{\"contentJson\":{V2()}}}"));

        Assert.Equal(160, titled.GetProperty("title").GetString()!.Length);
        Assert.StartsWith("Copy of ttt", titled.GetProperty("title").GetString());
        await AssertErrorAsync(unknownField, HttpStatusCode.BadRequest, "invalid_request");
        await AssertErrorAsync(badContent, HttpStatusCode.BadRequest, "validation_failed");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    // ---- attachments ----

    [Fact]
    public async Task Attachment_UploadSignedUrlAndFetch()
    {
        var client = await fixture.NewUserClientAsync();
        var assetId = await UploadAsync(client);

        var urlResponse = await client.GetAsync($"/api/notes/attachments/{assetId}/url");
        Assert.Equal(HttpStatusCode.OK, urlResponse.StatusCode);
        var signed = await Json(urlResponse);
        var expires = signed.GetProperty("expiresAtUtc").GetDateTime();
        Assert.InRange(expires, DateTime.UtcNow.AddMinutes(9), DateTime.UtcNow.AddMinutes(11));

        var url = new Uri(signed.GetProperty("url").GetString()!);
        var image = await fixture.AnonymousClient().GetAsync(url.PathAndQuery);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Png, await image.Content.ReadAsByteArrayAsync());

        var tampered = await fixture.AnonymousClient().GetAsync(url.PathAndQuery.Replace("sig=", "sig=AA"));
        Assert.Equal(HttpStatusCode.Forbidden, tampered.StatusCode);
    }

    [Fact]
    public async Task Attachment_ExpiredOrForeignUrl_IsRefused()
    {
        var owner = await fixture.NewUserClientAsync();
        var other = await fixture.NewUserClientAsync();
        var assetId = await UploadAsync(owner);

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/notes/attachments/{assetId}/url")).StatusCode);

        using var scope = fixture.CreateScope();
        var signer = scope.ServiceProvider.GetRequiredService<IAttachmentUrlSigner>();
        var expired = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds();
        var signature = signer.Sign(Guid.Parse(assetId), expired);
        var response = await fixture.AnonymousClient().GetAsync($"/api/notes/attachments/{assetId}/content?exp={expired}&sig={signature}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Attachment_UploadValidation()
    {
        var client = await fixture.NewUserClientAsync();
        var big = new byte[5 * 1024 * 1024 + 1];
        Png.CopyTo(big, 0);

        await AssertErrorAsync(await client.PostAsync("/api/notes/attachments", Upload(Encoding.UTF8.GetBytes("<svg/>"))), HttpStatusCode.BadRequest, "invalid_request");
        await AssertErrorAsync(await client.PostAsync("/api/notes/attachments", Upload(big)), HttpStatusCode.RequestEntityTooLarge, "payload_too_large");
        Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.AnonymousClient().PostAsync("/api/notes/attachments", Upload(Png))).StatusCode);
    }

    [Fact]
    public async Task NoteSave_RejectsImagesTheUserDoesNotOwn()
    {
        var owner = await fixture.NewUserClientAsync();
        var other = await fixture.NewUserClientAsync();
        var ownAsset = await UploadAsync(owner);
        var foreignAsset = await UploadAsync(other);

        var own = await owner.PostAsync("/api/notes", Raw(CreateBody("ok", V2(Image(ownAsset)))));
        var foreign = await owner.PostAsync("/api/notes", Raw(CreateBody("no", V2(Image(foreignAsset)))));
        var unknown = await owner.PostAsync("/api/notes", Raw(CreateBody("no", V2(Image(Guid.NewGuid().ToString())))));
        var ownNote = await Json(own);
        var patch = await owner.PatchAsync($"/api/notes/{Id(ownNote)}/content",
            Raw($"{{\"contentJson\":{V2(Image(foreignAsset))},\"versionToken\":\"{Token(ownNote)}\"}}"));

        Assert.Equal(HttpStatusCode.Created, own.StatusCode);
        await AssertErrorAsync(foreign, HttpStatusCode.BadRequest, "validation_failed");
        await AssertErrorAsync(unknown, HttpStatusCode.BadRequest, "validation_failed");
        await AssertErrorAsync(patch, HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task OrphanJob_DeletesAttachmentsUnreferencedFor7Days_AndKeepsReferencedOnes()
    {
        var client = await fixture.NewUserClientAsync();
        var orphan = Guid.Parse(await UploadAsync(client));
        var inDeletedNote = Guid.Parse(await UploadAsync(client));
        var inNote = Guid.Parse(await UploadAsync(client));
        await CreateNoteAsync(client, "keeps", V2(Image(inNote.ToString())));
        var trashed = await CreateNoteAsync(client, "trashed", V2(Image(inDeletedNote.ToString())));
        await client.DeleteAsync($"/api/notes/{Id(trashed)}");
        string? orphanObject = null;
        await fixture.WithDbAsync(async db => orphanObject = (await db.NoteAttachments.SingleAsync(a => a.Id == orphan)).ObjectName);

        using var scope = fixture.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
        var now = DateTime.UtcNow;
        await fixture.WithDbAsync(db => NotesMaintenance.CleanUpOrphanAttachmentsAsync(db, storage, now, CancellationToken.None));
        await fixture.WithDbAsync(async db => Assert.True(await db.NoteAttachments.AnyAsync(a => a.Id == orphan))); // only just noticed
        await fixture.WithDbAsync(db => NotesMaintenance.CleanUpOrphanAttachmentsAsync(db, storage, now.AddDays(8), CancellationToken.None));

        await fixture.WithDbAsync(async db =>
        {
            var remaining = await db.NoteAttachments.IgnoreQueryFilters().Where(a => a.Id == orphan || a.Id == inDeletedNote || a.Id == inNote).Select(a => a.Id).ToListAsync();
            Assert.Equal(new[] { inDeletedNote, inNote }.Order(), remaining.Order());
        });
        Assert.Null(await storage.OpenReadAsync(orphanObject, CancellationToken.None));
    }

    // ---- search ----

    [Fact]
    public async Task Search_MatchesTitleBodyChecklistTableAndGeorgian_WithRanges()
    {
        var client = await fixture.NewUserClientAsync();
        var table = "{\"type\":\"table\",\"content\":[{\"type\":\"tableRow\",\"content\":[{\"type\":\"tableCell\",\"content\":[" + Paragraph(Text("budget cell")) + "]}]}]}";
        await CreateNoteAsync(client, "Groceries", V2(Paragraph(Text("Buy მაწონი today")), Checklist("pick up parcel"), table));
        await CreateNoteAsync(client, "Other", V2(Paragraph(Text("nothing here"))));

        async Task<JsonElement> Search(string q) => await client.GetFromJsonAsync<JsonElement>($"/api/notes/search?q={Uri.EscapeDataString(q)}");

        Assert.Equal("Groceries", Assert.Single((await Search("groc")).EnumerateArray()).GetProperty("title").GetString());
        Assert.Single((await Search("PARCEL")).EnumerateArray());
        Assert.Single((await Search("budget")).EnumerateArray());
        Assert.Single((await Search("buy parcel")).EnumerateArray());
        Assert.Empty((await Search("buy nothing")).EnumerateArray());

        var hit = Assert.Single((await Search("წონ")).EnumerateArray());
        var snippet = hit.GetProperty("snippet").GetString()!;
        var range = Assert.Single(hit.GetProperty("snippetRanges").EnumerateArray());
        Assert.Equal("წონ", snippet.Substring(range.GetProperty("start").GetInt32(), range.GetProperty("length").GetInt32()));

        var title = Assert.Single((await Search("ceries")).EnumerateArray());
        var titleRange = Assert.Single(title.GetProperty("titleRanges").EnumerateArray());
        Assert.Equal(3, titleRange.GetProperty("start").GetInt32());
        Assert.Equal(6, titleRange.GetProperty("length").GetInt32());
    }

    [Fact]
    public async Task Search_EscapesWildcards_SkipsDeletedAndOtherUsers_AndLockedBodies()
    {
        var client = await fixture.NewUserClientAsync();
        var other = await fixture.NewUserClientAsync();
        await CreateNoteAsync(client, "plain", V2(Paragraph(Text("100% sure"))));
        await CreateNoteAsync(client, "Secret diary", V2(Paragraph(Text("hidden words"))), "\"isLocked\":true");
        var gone = await CreateNoteAsync(client, "gone", V2(Paragraph(Text("hidden too"))));
        await client.DeleteAsync($"/api/notes/{Id(gone)}");
        await CreateNoteAsync(other, "theirs", V2(Paragraph(Text("hidden there"))));

        async Task<JsonElement> Search(string q) => await client.GetFromJsonAsync<JsonElement>($"/api/notes/search?q={Uri.EscapeDataString(q)}");

        Assert.Single((await Search("0%")).EnumerateArray());
        Assert.Empty((await Search("1_0")).EnumerateArray());
        Assert.Empty((await Search("hidden")).EnumerateArray());
        var locked = Assert.Single((await Search("diary")).EnumerateArray());
        Assert.Equal("", locked.GetProperty("snippet").GetString());
        Assert.True(locked.GetProperty("isLocked").GetBoolean());
    }

    [Fact]
    public async Task Search_FollowsTitleTextAndLockChanges()
    {
        var client = await fixture.NewUserClientAsync();
        var note = await CreateNoteAsync(client, "Recipe", V2(Paragraph(Text("ᲛᲐᲬᲝᲜᲘ and honey"))));
        async Task<int> Hits(string q) => (await client.GetFromJsonAsync<JsonElement>($"/api/notes/search?q={Uri.EscapeDataString(q)}")).GetArrayLength();

        Assert.Equal(1, await Hits("მაწონი")); // Georgian capitals (Mtavruli) match their regular letters
        Assert.Equal(1, await Hits("HONEY"));

        var locked = await Json(await client.PutAsync($"/api/notes/{Id(note)}",
            Raw($"{{\"title\":\"Dessert\",{Metadata},\"isLocked\":true,\"versionToken\":\"{Token(note)}\"}}")));
        Assert.Equal(0, await Hits("honey"));
        Assert.Equal(0, await Hits("recipe"));
        Assert.Equal(1, await Hits("dessert"));

        await client.PutAsync($"/api/notes/{Id(note)}",
            Raw($"{{\"title\":\"Dessert\",{Metadata},\"isLocked\":false,\"versionToken\":\"{Token(locked)}\"}}"));
        Assert.Equal(1, await Hits("honey"));

        var duplicate = await client.PostAsync($"/api/notes/{Id(note)}/duplicate", Raw("{}"));
        Assert.Equal(HttpStatusCode.Created, duplicate.StatusCode);
        Assert.Equal(2, await Hits("honey"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("%20%20")]
    public async Task Search_BlankQuery_Returns400(string q)
    {
        var client = await fixture.NewUserClientAsync();

        await AssertErrorAsync(await client.GetAsync($"/api/notes/search?q={q}"), HttpStatusCode.BadRequest, "invalid_request");
        await AssertErrorAsync(await client.GetAsync($"/api/notes/search?q={new string('a', 101)}"), HttpStatusCode.BadRequest, "invalid_request");
    }

    // ---- backlinks ----

    [Fact]
    public async Task Backlinks_ListNotesLinkingHere()
    {
        var client = await fixture.NewUserClientAsync();
        var target = await CreateNoteAsync(client, "Target");
        var targetId = Id(target);
        await CreateNoteAsync(client, "Links to target", V2(Paragraph(NoteLink("see", targetId))));
        var deletedLinker = await CreateNoteAsync(client, "Deleted linker", V2(Paragraph(NoteLink("see", targetId))));
        await client.DeleteAsync($"/api/notes/{Id(deletedLinker)}");
        await CreateNoteAsync(client, "Unrelated");
        var self = await client.PatchAsync($"/api/notes/{targetId}/content",
            Raw($"{{\"contentJson\":{V2(Paragraph(NoteLink("me", targetId)))},\"versionToken\":\"{Token(target)}\"}}"));
        Assert.Equal(HttpStatusCode.OK, self.StatusCode);

        var backlinks = await client.GetFromJsonAsync<JsonElement>($"/api/notes/{targetId}/backlinks");

        Assert.Equal("Links to target", Assert.Single(backlinks.EnumerateArray()).GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await (await fixture.NewUserClientAsync()).GetAsync($"/api/notes/{targetId}/backlinks")).StatusCode);
    }

    // ---- app config ----

    [Fact]
    public async Task AppConfig_HasIosStoreUrl()
    {
        var defaults = await fixture.AnonymousClient().GetFromJsonAsync<JsonElement>("/api/app/config");
        var configured = await fixture.ClientWithSettings(("ClientApp:IosStoreUrl", "https://apps.apple.com/app/id1"))
            .GetFromJsonAsync<JsonElement>("/api/app/config");

        Assert.Equal("", defaults.GetProperty("iosStoreUrl").GetString());
        Assert.Equal("https://apps.apple.com/app/id1", configured.GetProperty("iosStoreUrl").GetString());
    }
}
