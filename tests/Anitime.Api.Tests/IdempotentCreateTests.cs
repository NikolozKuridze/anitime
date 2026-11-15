using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Anitime.Api.Tests;

/// <summary>
/// The app's offline outbox retries creates after network failures. A repeat with the same clientRequestId returns
/// the original item (200) instead of creating a second one, like task create does.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class IdempotentCreateTests(ApiFixture fixture)
{
    public static TheoryData<string> Endpoints => ["/api/habits", "/api/reminders", "/api/notes"];

    private static Task<JsonElement> Json(HttpResponseMessage response) => response.Content.ReadFromJsonAsync<JsonElement>();
    private static string Id(JsonElement element) => element.GetProperty("id").GetString()!;
    private static string NewRequestId() => Guid.NewGuid().ToString("N");

    private static Dictionary<string, object?> Body(string endpoint, string? clientRequestId, string title = "Item")
    {
        Dictionary<string, object?> body = endpoint switch
        {
            "/api/habits" => new() { ["title"] = title, ["emoji"] = "🔥", ["durationDays"] = 30 },
            "/api/reminders" => new()
            {
                ["title"] = title, ["message"] = "m", ["scheduledForUtc"] = DateTime.UtcNow.AddHours(1), ["repeatDaily"] = false
            },
            _ => new()
            {
                ["title"] = title,
                ["contentJson"] = new
                {
                    version = 1,
                    blocks = new[] { new { type = "paragraph", align = "left", children = new[] { new { textNode = new { type = "text", text = "x" } } } } }
                },
                ["plainTextPreview"] = "x",
                ["fontFamily"] = "system-ui",
                ["fontSize"] = 16,
                ["fontWeight"] = 400,
                ["textColor"] = "#111827",
                ["backgroundColor"] = "#FFFFFF",
                ["isPinned"] = false,
                ["isArchived"] = false
            }
        };
        if (clientRequestId is not null)
        {
            body["clientRequestId"] = clientRequestId;
        }

        return body;
    }

    private static async Task<int> CountAsync(HttpClient client, string endpoint) =>
        (await client.GetFromJsonAsync<JsonElement>(endpoint)).GetArrayLength();

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Repeat_ReturnsTheOriginalWith200_AndCreatesNothing(string endpoint)
    {
        var client = await fixture.NewUserClientAsync();
        var requestId = NewRequestId();

        var first = await client.PostAsJsonAsync(endpoint, Body(endpoint, requestId, "Original"));
        var repeat = await client.PostAsJsonAsync(endpoint, Body(endpoint, requestId, "Changed on retry"));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        var original = await Json(first);
        var repeated = await Json(repeat);
        Assert.Equal(Id(original), Id(repeated));
        Assert.Equal("Original", repeated.GetProperty("title").GetString());
        Assert.Equal(1, await CountAsync(client, endpoint));
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task ParallelRepeats_CreateOnlyOne(string endpoint)
    {
        var client = await fixture.NewUserClientAsync();
        var body = Body(endpoint, NewRequestId());

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => client.PostAsJsonAsync(endpoint, body)));

        Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode, $"got {(int)r.StatusCode}"));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        var ids = await Task.WhenAll(responses.Select(async r => Id(await Json(r))));
        Assert.Single(ids.Distinct());
        Assert.Equal(1, await CountAsync(client, endpoint));
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task WithoutClientRequestId_EveryPostCreates(string endpoint)
    {
        var client = await fixture.NewUserClientAsync();

        var first = await client.PostAsJsonAsync(endpoint, Body(endpoint, null));
        var second = await client.PostAsJsonAsync(endpoint, Body(endpoint, null));
        var blank = await client.PostAsJsonAsync(endpoint, Body(endpoint, "  "));
        var blankAgain = await client.PostAsJsonAsync(endpoint, Body(endpoint, "  "));

        Assert.All([first, second, blank, blankAgain], r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Equal(4, await CountAsync(client, endpoint));
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task SameClientRequestId_FromAnotherUser_CreatesTheirOwn(string endpoint)
    {
        var owner = await fixture.NewUserClientAsync();
        var other = await fixture.NewUserClientAsync();
        var requestId = NewRequestId();

        var mine = await owner.PostAsJsonAsync(endpoint, Body(endpoint, requestId));
        var theirs = await other.PostAsJsonAsync(endpoint, Body(endpoint, requestId));

        Assert.Equal(HttpStatusCode.Created, mine.StatusCode);
        Assert.Equal(HttpStatusCode.Created, theirs.StatusCode);
        Assert.NotEqual(Id(await Json(mine)), Id(await Json(theirs)));
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task ClientRequestId_ReusedAfterDelete_CreatesNew(string endpoint)
    {
        var client = await fixture.NewUserClientAsync();
        var requestId = NewRequestId();
        var first = Id(await Json(await client.PostAsJsonAsync(endpoint, Body(endpoint, requestId))));
        (await client.DeleteAsync($"{endpoint}/{first}")).EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync(endpoint, Body(endpoint, requestId));

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.NotEqual(first, Id(await Json(second)));
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task ClientRequestId_LongerThan64_Returns400(string endpoint)
    {
        var client = await fixture.NewUserClientAsync();

        var response = await client.PostAsJsonAsync(endpoint, Body(endpoint, new string('a', 65)));
        var longest = await client.PostAsJsonAsync(endpoint, Body(endpoint, new string('a', 64)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", (await Json(response)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Created, longest.StatusCode);
    }

    [Fact]
    public async Task ReminderRetry_AfterItsTimeHasPassed_StillReturnsTheOriginal()
    {
        // An outbox can hold a reminder offline past its scheduled time; the retry must not fail validation.
        var client = await fixture.NewUserClientAsync();
        var requestId = NewRequestId();
        var original = await Json(await client.PostAsJsonAsync("/api/reminders", Body("/api/reminders", requestId)));
        var late = Body("/api/reminders", requestId);
        late["scheduledForUtc"] = DateTime.UtcNow.AddHours(-2);

        var retry = await client.PostAsJsonAsync("/api/reminders", late);

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(Id(original), Id(await Json(retry)));
    }

    [Fact]
    public async Task HabitRepeat_ReturnsTheCurrentStreak()
    {
        var client = await fixture.NewUserClientAsync();
        var requestId = NewRequestId();
        var habit = await Json(await client.PostAsJsonAsync("/api/habits", Body("/api/habits", requestId)));
        (await client.PostAsJsonAsync($"/api/habits/{Id(habit)}/check", new { })).EnsureSuccessStatusCode();

        var repeated = await Json(await client.PostAsJsonAsync("/api/habits", Body("/api/habits", requestId)));

        Assert.Equal(1, repeated.GetProperty("currentStreak").GetInt32());
    }

    [Fact]
    public async Task RestoringADeletedNote_WhoseClientRequestIdWasReused_KeepsBothNotes()
    {
        var client = await fixture.NewUserClientAsync();
        var requestId = NewRequestId();
        var first = Id(await Json(await client.PostAsJsonAsync("/api/notes", Body("/api/notes", requestId))));
        (await client.DeleteAsync($"/api/notes/{first}")).EnsureSuccessStatusCode();
        var second = Id(await Json(await client.PostAsJsonAsync("/api/notes", Body("/api/notes", requestId))));

        var restore = await client.PostAsync($"/api/notes/{first}/restore", null);
        var retry = await client.PostAsJsonAsync("/api/notes", Body("/api/notes", requestId));

        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        Assert.Equal(2, await CountAsync(client, "/api/notes"));
        Assert.Equal(second, Id(await Json(retry))); // the key stays with the note that holds it
    }
}
