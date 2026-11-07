using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Anitime.Api.Tests;

/// <summary>Races outside notes: duplicate requests must not surface as HTTP 500.</summary>
[Collection(ApiCollection.Name)]
public sealed class ConcurrencyApiTests(ApiFixture fixture)
{
    [Fact]
    public async Task ParallelSignupsWithSameEmail_OneSucceeds_OthersGet409()
    {
        var client = fixture.AnonymousClient();
        var email = $"race-{Guid.NewGuid():N}@example.com";
        var body = new { email, password = "Test-Password-123", name = "Racer", gender = "Male" };

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => client.PostAsJsonAsync("/api/auth/signup", body)));

        var statuses = responses.Select(r => r.StatusCode).ToList();
        Assert.Equal(1, statuses.Count(s => s == HttpStatusCode.OK));
        Assert.Equal(5, statuses.Count(s => s == HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task ParallelFirstHabitChecks_NeverReturn500()
    {
        var client = await fixture.NewUserClientAsync();
        var create = await client.PostAsJsonAsync("/api/habits", new { title = "Run", emoji = "🏃", durationDays = 30 });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            client.PostAsJsonAsync($"/api/habits/{id}/check", new { })));

        Assert.All(responses, r => Assert.True((int)r.StatusCode < 500, $"got {(int)r.StatusCode}"));
    }

    [Fact]
    public async Task UnhandledException_Returns500WithJsonErrorBody()
    {
        var healthy = await fixture.NewUserClientAsync();
        var client = fixture.ClientWithFailingCache(healthy.DefaultRequestHeaders.Authorization!.Parameter!);

        var response = await client.PostAsJsonAsync("/api/habits", new { title = "Run", emoji = "🏃", durationDays = 30 });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("internal_error", body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task ParallelTaskCreatesWithSameClientRequestId_CreateOnlyOneTask()
    {
        var client = await fixture.NewUserClientAsync();
        var body = new { title = "Task", date = "2026-11-07", clientRequestId = Guid.NewGuid().ToString("N") };

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => client.PostAsJsonAsync("/api/tasks", body)));

        Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode, $"got {(int)r.StatusCode}"));
        Assert.Equal(1, (await client.GetFromJsonAsync<JsonElement>("/api/tasks")).GetArrayLength());
    }
}
