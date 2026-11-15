using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Anitime.Api.Tests;

/// <summary>Release 1 additions: task priority, checklist edit/delete, reminder update, photo removal.</summary>
[Collection(ApiCollection.Name)]
public sealed class ReleaseApiTests(ApiFixture fixture)
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 1];
    private static Task<JsonElement> Json(HttpResponseMessage response) => response.Content.ReadFromJsonAsync<JsonElement>();
    private static string Id(JsonElement element) => element.GetProperty("id").GetString()!;
    private static string Today => DateTime.UtcNow.ToString("yyyy-MM-dd");

    private static async Task<JsonElement> CreateTaskAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/tasks", body);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await Json(response);
    }

    // ---- task priority ----

    [Fact]
    public async Task TaskPriority_DefaultsToMedium_AndIsReturnedAsPriorityAndPriorityLabel()
    {
        var client = await fixture.NewUserClientAsync();

        var task = await CreateTaskAsync(client, new { title = "t", date = Today });

        Assert.Equal("Medium", task.GetProperty("priority").GetString());
        Assert.Equal("Medium", task.GetProperty("priorityLabel").GetString());
    }

    [Fact]
    public async Task TaskPriority_IsAcceptedOnCreateAndUpdate_CaseInsensitive()
    {
        var client = await fixture.NewUserClientAsync();
        var task = await CreateTaskAsync(client, new { title = "t", date = Today, priority = "high" });
        Assert.Equal("High", task.GetProperty("priorityLabel").GetString());

        var updated = await Json(await client.PutAsJsonAsync($"/api/tasks/{Id(task)}", new { title = "t", date = Today, priority = "Low" }));

        Assert.Equal("Low", updated.GetProperty("priorityLabel").GetString());
        var listed = (await client.GetFromJsonAsync<JsonElement>("/api/tasks"))[0];
        Assert.Equal("Low", listed.GetProperty("priority").GetString());
    }

    [Fact]
    public async Task InstalledAppUpdate_WithoutPriority_KeepsTheStoredPriority()
    {
        var client = await fixture.NewUserClientAsync();
        var task = await CreateTaskAsync(client, new { title = "t", date = Today, priority = "High" });

        // Today's app sends only title/description/emoji/date (UpdateTaskRequest = Partial<CreateTaskRequest>).
        var omitted = await Json(await client.PutAsJsonAsync($"/api/tasks/{Id(task)}", new { title = "renamed", description = "d", emoji = "✅", date = Today }));
        var nulled = await Json(await client.PutAsJsonAsync($"/api/tasks/{Id(task)}", new { title = "renamed", date = Today, priority = (string?)null }));

        Assert.Equal("High", omitted.GetProperty("priorityLabel").GetString());
        Assert.Equal("High", nulled.GetProperty("priorityLabel").GetString());
        Assert.Equal("renamed", nulled.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("urgent")]
    [InlineData("")]
    public async Task InvalidPriority_Returns400(string priority)
    {
        var client = await fixture.NewUserClientAsync();

        var response = await client.PostAsJsonAsync("/api/tasks", new { title = "t", date = Today, priority });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", (await Json(response)).GetProperty("code").GetString());
    }

    // ---- checklist edit and delete ----

    [Fact]
    public async Task ChecklistItem_CanBeRenamed_AndDeleted()
    {
        var client = await fixture.NewUserClientAsync();
        var task = await CreateTaskAsync(client, new { title = "t", date = Today, checklistItems = new[] { new { title = "a" }, new { title = "b" } } });
        var items = task.GetProperty("checklistItems");
        var first = Id(items[0]);
        var second = Id(items[1]);

        var renamed = await client.PutAsJsonAsync($"/api/tasks/{Id(task)}/checklist/{first}", new { title = "  milk  " });
        var deleted = await client.DeleteAsync($"/api/tasks/{Id(task)}/checklist/{second}");

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal("milk", (await Json(renamed)).GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var stored = (await client.GetFromJsonAsync<JsonElement>("/api/tasks"))[0].GetProperty("checklistItems");
        Assert.Equal("milk", Assert.Single(stored.EnumerateArray()).GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/tasks/{Id(task)}/checklist/{second}")).StatusCode);
    }

    [Fact]
    public async Task ChecklistItem_OfAnotherUserOrTask_Returns404()
    {
        var owner = await fixture.NewUserClientAsync();
        var other = await fixture.NewUserClientAsync();
        var task = await CreateTaskAsync(owner, new { title = "t", date = Today, checklistItems = new[] { new { title = "a" } } });
        var otherTask = await CreateTaskAsync(owner, new { title = "u", date = Today });
        var item = Id(task.GetProperty("checklistItems")[0]);

        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/tasks/{Id(task)}/checklist/{item}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/api/tasks/{Id(task)}/checklist/{item}", new { title = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.DeleteAsync($"/api/tasks/{Id(otherTask)}/checklist/{item}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync($"/api/tasks/{Id(task)}/checklist/{item}", new { title = " " })).StatusCode);
    }

    // ---- reminder update ----

    [Fact]
    public async Task Reminder_UpdateKeepsItsId()
    {
        var client = await fixture.NewUserClientAsync();
        var created = await Json(await client.PostAsJsonAsync("/api/reminders",
            new { title = "Walk", message = "Go", scheduledForUtc = DateTime.UtcNow.AddHours(1), repeatDaily = false }));
        var newTime = DateTime.UtcNow.AddHours(3);

        var response = await client.PutAsJsonAsync($"/api/reminders/{Id(created)}",
            new { title = "Run", message = "Now", scheduledForUtc = newTime, repeatDaily = true, isEnabled = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await Json(response);
        Assert.Equal(Id(created), Id(updated));
        Assert.Equal("Run", updated.GetProperty("title").GetString());
        Assert.True(updated.GetProperty("repeatDaily").GetBoolean());
        Assert.False(updated.GetProperty("isEnabled").GetBoolean());
        var listed = Assert.Single((await client.GetFromJsonAsync<JsonElement>("/api/reminders")).EnumerateArray());
        Assert.Equal(Id(created), Id(listed));
    }

    [Fact]
    public async Task Reminder_Update_ValidatesLikeCreate_AndIsOwnerOnly()
    {
        var client = await fixture.NewUserClientAsync();
        var other = await fixture.NewUserClientAsync();
        var created = await Json(await client.PostAsJsonAsync("/api/reminders",
            new { title = "Walk", message = "Go", scheduledForUtc = DateTime.UtcNow.AddHours(1), repeatDaily = false }));
        var url = $"/api/reminders/{Id(created)}";

        var past = await client.PutAsJsonAsync(url, new { title = "x", message = "y", scheduledForUtc = DateTime.UtcNow.AddHours(-2), repeatDaily = false, isEnabled = true });
        var blank = await client.PutAsJsonAsync(url, new { title = "", message = "y", scheduledForUtc = DateTime.UtcNow.AddHours(2), repeatDaily = false, isEnabled = true });
        var foreign = await other.PutAsJsonAsync(url, new { title = "x", message = "y", scheduledForUtc = DateTime.UtcNow.AddHours(2), repeatDaily = false, isEnabled = true });

        Assert.Equal(HttpStatusCode.BadRequest, past.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    // ---- remove photos ----

    private static MultipartFormDataContent Image()
    {
        var content = new ByteArrayContent(Jpeg);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return new MultipartFormDataContent { { content, "file", "photo.jpg" } };
    }

    [Theory]
    [InlineData("profile-image", "hasProfileImage", "profileImageUrl")]
    [InlineData("background-image", "hasBackgroundImage", "backgroundImageUrl")]
    public async Task Photo_CanBeRemoved_AndTheProfileSaysSo(string path, string flag, string urlField)
    {
        var client = await fixture.NewUserClientAsync();
        Assert.False((await client.GetFromJsonAsync<JsonElement>("/api/profile")).GetProperty(flag).GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/profile/{path}", Image())).StatusCode);

        var withPhoto = await client.GetFromJsonAsync<JsonElement>("/api/profile");
        Assert.True(withPhoto.GetProperty(flag).GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/profile/{path}")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/profile/{path}")).StatusCode);
        var without = await client.GetFromJsonAsync<JsonElement>("/api/profile"); // the cached profile was invalidated
        Assert.False(without.GetProperty(flag).GetBoolean());
        Assert.Equal(JsonValueKind.Null, without.GetProperty(urlField).ValueKind);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/profile/{path}")).StatusCode); // idempotent
    }

    // ---- weekly summary comes from the existing analytics endpoint ----

    [Fact]
    public async Task WeeklySummary_IsServedByAnalyticsForTheLast7Days()
    {
        var client = await fixture.NewUserClientAsync();
        var task = await CreateTaskAsync(client, new { title = "t", date = Today });
        await client.PostAsJsonAsync($"/api/tasks/{Id(task)}/toggle", new { isCompleted = true });
        var from = DateTime.UtcNow.AddDays(-6).ToString("yyyy-MM-dd");

        var week = await client.GetFromJsonAsync<JsonElement>($"/api/analytics?from={from}&to={Today}");

        var days = week.GetProperty("daily");
        Assert.Equal(7, days.GetArrayLength());
        Assert.Equal(1, days.EnumerateArray().Sum(d => d.GetProperty("tasksCompleted").GetInt32()));
        Assert.Equal(0, days.EnumerateArray().Sum(d => d.GetProperty("habitCompletions").GetInt32()));
        Assert.True(week.TryGetProperty("bestStreak", out _));
    }
}
