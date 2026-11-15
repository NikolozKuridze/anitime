using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Anitime.Api.Tests;

/// <summary>
/// Fields the app treats as optional must accept a missing value, null and "". A non-nullable string in a request
/// record gets an implicit [Required] from ASP.NET, which also rejects "" ("The Message field is required.").
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class OptionalFieldsTests(ApiFixture fixture)
{
    private const string Omit = "<omit>";

    public static TheoryData<string?> EmptyValues => [Omit, null, "", "   "];

    private static Task<JsonElement> Json(HttpResponseMessage response) => response.Content.ReadFromJsonAsync<JsonElement>();
    private static string Id(JsonElement element) => element.GetProperty("id").GetString()!;

    private static Dictionary<string, object?> With(Dictionary<string, object?> body, string field, string? value)
    {
        if (value != Omit)
        {
            body[field] = value;
        }

        return body;
    }

    private static Dictionary<string, object?> ReminderCreate(string? message) => With(new()
    {
        ["title"] = "Walk", ["scheduledForUtc"] = DateTime.UtcNow.AddHours(1), ["repeatDaily"] = false
    }, "message", message);

    private static Dictionary<string, object?> ReminderUpdate(string? message, bool isEnabled = true) => With(new()
    {
        ["title"] = "Walk", ["scheduledForUtc"] = DateTime.UtcNow.AddHours(2), ["repeatDaily"] = false, ["isEnabled"] = isEnabled
    }, "message", message);

    private static async Task AssertCreated(HttpResponseMessage response) =>
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

    private static async Task AssertOk(HttpResponseMessage response) =>
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

    // ---- reminder message ----

    [Theory]
    [MemberData(nameof(EmptyValues))]
    public async Task ReminderCreate_WithoutMessage_StoresEmptyMessage(string? message)
    {
        var client = await fixture.NewUserClientAsync();

        var response = await client.PostAsJsonAsync("/api/reminders", ReminderCreate(message));

        await AssertCreated(response);
        Assert.Equal("", (await Json(response)).GetProperty("message").GetString());
        var listed = Assert.Single((await client.GetFromJsonAsync<JsonElement>("/api/reminders")).EnumerateArray());
        Assert.Equal("", listed.GetProperty("message").GetString());
    }

    [Theory]
    [MemberData(nameof(EmptyValues))]
    public async Task ReminderUpdate_WithoutMessage_StoresEmptyMessage(string? message)
    {
        var client = await fixture.NewUserClientAsync();
        var created = await Json(await client.PostAsJsonAsync("/api/reminders", ReminderCreate("Go outside")));

        var response = await client.PutAsJsonAsync($"/api/reminders/{Id(created)}", ReminderUpdate(message));

        await AssertOk(response);
        Assert.Equal("", (await Json(response)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task PausingAReminderWithoutMessage_Works()
    {
        var client = await fixture.NewUserClientAsync();
        var created = await Json(await client.PostAsJsonAsync("/api/reminders", ReminderCreate("")));

        var paused = await client.PutAsJsonAsync($"/api/reminders/{Id(created)}", ReminderUpdate("", isEnabled: false));
        var toggled = await client.PostAsJsonAsync($"/api/reminders/{Id(created)}/toggle", new { isEnabled = true });

        await AssertOk(paused);
        Assert.False((await Json(paused)).GetProperty("isEnabled").GetBoolean());
        await AssertOk(toggled);
    }

    [Fact]
    public async Task ReminderMessage_IsTrimmed_AndStillLimitedTo1000()
    {
        var client = await fixture.NewUserClientAsync();

        var trimmed = await client.PostAsJsonAsync("/api/reminders", ReminderCreate("  Go  "));
        var tooLong = await client.PostAsJsonAsync("/api/reminders", ReminderCreate(new string('m', 1001)));
        var longest = await client.PostAsJsonAsync("/api/reminders", ReminderCreate(new string('m', 1000)));

        Assert.Equal("Go", (await Json(trimmed)).GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal("validation_failed", (await Json(tooLong)).GetProperty("code").GetString());
        await AssertCreated(longest);
    }

    [Fact]
    public async Task ReminderTitle_IsStillRequired()
    {
        var client = await fixture.NewUserClientAsync();
        var body = ReminderCreate("m");
        body["title"] = "";

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/reminders", body)).StatusCode);
    }

    // ---- habit emoji ----

    [Theory]
    [MemberData(nameof(EmptyValues))]
    public async Task HabitCreate_WithoutEmoji_UsesTheDefault(string? emoji)
    {
        var client = await fixture.NewUserClientAsync();

        var response = await client.PostAsJsonAsync("/api/habits", With(new() { ["title"] = "Read", ["durationDays"] = 30 }, "emoji", emoji));

        await AssertCreated(response);
        Assert.Equal("🔥", (await Json(response)).GetProperty("emoji").GetString());
    }

    // ---- task description and emoji (already optional; locked in) ----

    [Theory]
    [MemberData(nameof(EmptyValues))]
    public async Task TaskCreateAndUpdate_WithoutDescriptionOrEmoji_Work(string? value)
    {
        var client = await fixture.NewUserClientAsync();
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var body = With(With(new() { ["title"] = "t", ["date"] = today }, "description", value), "emoji", value);

        var created = await client.PostAsJsonAsync("/api/tasks", body);
        await AssertCreated(created);
        var updated = await client.PutAsJsonAsync($"/api/tasks/{Id(await Json(created))}", body);

        await AssertOk(updated);
    }

    // ---- notes ----

    [Fact]
    public async Task Note_WithAnEmptyTextNode_IsAccepted()
    {
        // An empty line in a version 1 note is a text node with text "".
        var client = await fixture.NewUserClientAsync();
        var body = new Dictionary<string, object?>
        {
            ["title"] = "Title",
            ["contentJson"] = new
            {
                version = 1,
                blocks = new object[]
                {
                    new { type = "paragraph", align = "left", children = new[] { new { textNode = new { type = "text", text = "first" } } } },
                    new { type = "paragraph", align = "left", children = new[] { new { textNode = new { type = "text", text = "" } } } }
                }
            },
            ["plainTextPreview"] = "first",
            ["fontFamily"] = "system-ui",
            ["fontSize"] = 16,
            ["fontWeight"] = 400,
            ["textColor"] = "#111827",
            ["backgroundColor"] = "#FFFFFF",
            ["isPinned"] = false,
            ["isArchived"] = false
        };

        await AssertCreated(await client.PostAsJsonAsync("/api/notes", body));
    }

    [Theory]
    [MemberData(nameof(EmptyValues))]
    public async Task Note_WithoutTitleOrStyleFields_IsAccepted(string? value)
    {
        var client = await fixture.NewUserClientAsync();
        var body = new Dictionary<string, object?>
        {
            ["contentJson"] = new
            {
                version = 1,
                blocks = new[] { new { type = "paragraph", align = "left", children = new[] { new { textNode = new { type = "text", text = "x" } } } } }
            },
            ["fontSize"] = 16,
            ["fontWeight"] = 400,
            ["isPinned"] = false,
            ["isArchived"] = false
        };
        foreach (var field in new[] { "title", "plainTextPreview", "fontFamily", "textColor", "backgroundColor" })
        {
            With(body, field, value);
        }

        await AssertCreated(await client.PostAsJsonAsync("/api/notes", body));
    }
}
