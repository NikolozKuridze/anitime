using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Anitime.Application.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Anitime.Api.Tests;

/// <summary>
/// Guards the hot endpoints the app calls on startup and per tab: SQL commands per request must stay within budget
/// and must not grow with the amount of data (no N+1), and lists must not over-fetch columns they don't return.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PerformanceApiTests(ApiFixture fixture, ITestOutputHelper output)
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static (HttpClient Client, Guid UserId)? _small;
    private static (HttpClient Client, Guid UserId)? _large;

    /// <summary>Endpoint name → (path, max SQL commands on a cache miss).</summary>
    public static readonly Dictionary<string, (string Path, int MaxQueries)> HotEndpoints = new()
    {
        ["profile"] = ("/api/profile", 1),
        ["dashboard"] = ("/api/dashboard", 2),
        ["analytics"] = ("/api/analytics", 2),
        ["calendar"] = ($"/api/calendar?year={DateTime.UtcNow.Year}&month={DateTime.UtcNow.Month}", 2),
        ["tasks"] = ("/api/tasks", 1),
        ["tasks (7-day range)"] = ($"/api/tasks?from={DateTime.UtcNow.AddDays(-6):yyyy-MM-dd}&to={DateTime.UtcNow:yyyy-MM-dd}", 1),
        ["habits"] = ("/api/habits", 1),
        ["reminders"] = ("/api/reminders", 1),
        ["notes"] = ("/api/notes", 1),
        ["notes (summary view)"] = ("/api/notes?view=summary", 1),
        ["exercises"] = ("/api/exercises?page=1&pageSize=20", 2),
        ["workouts"] = ("/api/workouts", 1)
    };

    public static TheoryData<string> HotEndpointNames() => new(HotEndpoints.Keys);

    private async Task<(HttpClient Client, Guid UserId)> UserAsync(bool large)
    {
        await SeedLock.WaitAsync();
        try
        {
            if (large)
            {
                return _large ??= await PerformanceSeed.SeedUserAsync(fixture, SeedSize.Large);
            }

            return _small ??= await PerformanceSeed.SeedUserAsync(fixture, SeedSize.Small);
        }
        finally
        {
            SeedLock.Release();
        }
    }

    private async Task ClearUserCacheAsync(Guid userId)
    {
        using var scope = fixture.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        await cache.RemoveAsync(CacheKeys.Dashboard(userId));
        await cache.RemoveAsync(CacheKeys.Profile(userId));
        await cache.RemoveAsync(CacheKeys.ExerciseFilters);
    }

    private async Task<QueryCounter.Scope> CountAsync(HttpClient client, Guid userId, string path)
    {
        await ClearUserCacheAsync(userId);
        var scope = QueryCounter.Begin();
        var response = await client.GetAsync(path);
        scope.Dispose();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(scope.Commands); // a broken counter would otherwise make every budget pass
        return scope;
    }

    [Theory]
    [MemberData(nameof(HotEndpointNames))]
    public async Task HotEndpoint_StaysWithinQueryBudget_AndDoesNotGrowWithData(string name)
    {
        var (path, maxQueries) = HotEndpoints[name];
        var small = await UserAsync(large: false);
        var large = await UserAsync(large: true);

        var smallQueries = await CountAsync(small.Client, small.UserId, path);
        var largeQueries = await CountAsync(large.Client, large.UserId, path);

        output.WriteLine($"{name}: {smallQueries.Count} small, {largeQueries.Count} large\n{largeQueries}");
        Assert.True(largeQueries.Count <= maxQueries, $"{name} ran {largeQueries.Count} queries (budget {maxQueries}):\n{largeQueries}");
        Assert.Equal(smallQueries.Count, largeQueries.Count);
    }

    [Theory]
    [InlineData("/api/dashboard")]
    [InlineData("/api/profile")]
    public async Task CachedEndpoint_SecondCall_RunsNoQueries(string path)
    {
        var (client, _) = await UserAsync(large: true);
        (await client.GetAsync(path)).EnsureSuccessStatusCode();

        using var scope = QueryCounter.Begin();
        (await client.GetAsync(path)).EnsureSuccessStatusCode();

        Assert.Equal(0, scope.Count);
    }

    [Theory]
    [InlineData("dashboard")]
    [InlineData("analytics")]
    [InlineData("calendar")]
    public async Task Aggregates_DoNotReadTaskTextColumns(string name)
    {
        var (client, userId) = await UserAsync(large: true);

        var queries = await CountAsync(client, userId, HotEndpoints[name].Path);

        Assert.DoesNotContain(queries.Commands, sql => sql.Contains("\"Description\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HabitCheck_LoadsOnlyTheTargetDay_NotTheWholeHistory()
    {
        var (client, userId) = await UserAsync(large: true);
        var habit = (await client.GetFromJsonAsync<JsonElement>("/api/habits"))[0];
        var id = habit.GetProperty("id").GetString();
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

        var scope = QueryCounter.Begin();
        var first = await client.PostAsJsonAsync($"/api/habits/{id}/check", new { date = today });
        scope.Dispose();
        (await client.PostAsJsonAsync($"/api/habits/{id}/check", new { date = today })).EnsureSuccessStatusCode(); // restore

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var load = scope.Commands.First(sql => sql.Contains("habit_entries", StringComparison.Ordinal));
        Assert.Contains("\"Date\" = ", load, StringComparison.Ordinal);
        await ClearUserCacheAsync(userId);
    }

    // ---- response size ----

    [Fact]
    public async Task JsonResponses_AreCompressed_WhenTheClientAcceptsIt()
    {
        var (client, _) = await UserAsync(large: true);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/notes");
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("br", response.Content.Headers.ContentEncoding);
    }

    [Fact]
    public async Task AuthResponses_AreNotCompressed()
    {
        // Tokens in a compressed response next to attacker-influenced input is the BREACH pattern; auth stays plain.
        var client = fixture.AnonymousClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/signup")
        {
            Content = JsonContent.Create(new { email = $"zip-{Guid.NewGuid():N}@example.com", password = "Test-Password-123", name = "Zip", gender = "Male" })
        };
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(response.Content.Headers.ContentEncoding);
    }

    [Theory]
    [InlineData("/api/notes")]
    [InlineData("/api/tasks")]
    [InlineData("/api/habits")]
    [InlineData("/api/reminders")]
    public async Task Lists_SupportConditionalGet(string path)
    {
        var (client, _) = await UserAsync(large: true);

        var first = await client.GetAsync(path);
        var etag = first.Headers.ETag;
        Assert.NotNull(etag);
        Assert.Contains("no-cache", first.Headers.CacheControl!.ToString());
        Assert.True(first.Headers.CacheControl.Private);

        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.IfNoneMatch.Add(etag);
        var second = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
        Assert.Equal(0, (await second.Content.ReadAsByteArrayAsync()).Length);
    }

    [Fact]
    public async Task ConditionalGet_ReturnsFreshData_AfterAChange()
    {
        var client = await fixture.NewUserClientAsync();
        var first = await client.GetAsync("/api/reminders");
        var etag = first.Headers.ETag!;
        (await client.PostAsJsonAsync("/api/reminders", new
        {
            title = "Water", message = "Drink", scheduledForUtc = DateTime.UtcNow.AddHours(1), repeatDaily = false
        })).EnsureSuccessStatusCode();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/reminders");
        request.Headers.IfNoneMatch.Add(etag);
        var second = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.NotEqual(etag, second.Headers.ETag);
        Assert.Single((await second.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray());
    }

    [Fact]
    public async Task ConditionalGet_DoesNotLeakAcrossUsers()
    {
        var a = await fixture.NewUserClientAsync();
        var b = await fixture.NewUserClientAsync();
        (await a.PostAsJsonAsync("/api/reminders", new
        {
            title = "Mine", message = "A", scheduledForUtc = DateTime.UtcNow.AddHours(1), repeatDaily = false
        })).EnsureSuccessStatusCode();
        var etagA = (await a.GetAsync("/api/reminders")).Headers.ETag!;

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/reminders");
        request.Headers.IfNoneMatch.Add(etagA);
        var asB = await b.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, asB.StatusCode);
        Assert.Empty((await asB.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray());
    }

    // ---- additive list options (the shipped app is unaffected) ----

    [Fact]
    public async Task NotesSummaryView_OmitsContent_AndDoesNotReadIt()
    {
        var (client, userId) = await UserAsync(large: true);

        var full = await client.GetFromJsonAsync<JsonElement>("/api/notes");
        var queries = await CountAsync(client, userId, "/api/notes?view=summary");
        var summary = await client.GetFromJsonAsync<JsonElement>("/api/notes?view=summary");

        Assert.Equal(full.GetArrayLength(), summary.GetArrayLength());
        var first = summary[0];
        Assert.False(first.TryGetProperty("contentJson", out _));
        Assert.Equal(full[0].GetProperty("id").GetString(), first.GetProperty("id").GetString());
        Assert.Equal(full[0].GetProperty("versionToken").GetString(), first.GetProperty("versionToken").GetString());
        Assert.Equal(full[0].GetProperty("plainTextPreview").GetString(), first.GetProperty("plainTextPreview").GetString());
        Assert.DoesNotContain(queries.Commands, sql => sql.Contains("\"ContentJson\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NotesList_DefaultShape_IsUnchanged()
    {
        var (client, _) = await UserAsync(large: true);

        var note = (await client.GetFromJsonAsync<JsonElement>("/api/notes"))[0];

        var expected = new[]
        {
            "id", "title", "contentJson", "plainTextPreview", "fontFamily", "fontSize", "fontWeight", "textColor",
            "backgroundColor", "isPinned", "isArchived", "versionToken", "lastEditedAtUtc", "createdAtUtc", "updatedAtUtc"
        };
        Assert.Equal(expected, note.EnumerateObject().Select(p => p.Name));
        Assert.Equal(1, note.GetProperty("contentJson").GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task TasksList_CanBeLimitedToADateRange()
    {
        var (client, _) = await UserAsync(large: true);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = today.AddDays(-6);

        var all = await client.GetFromJsonAsync<JsonElement>("/api/tasks");
        var week = await client.GetFromJsonAsync<JsonElement>($"/api/tasks?from={from:yyyy-MM-dd}&to={today:yyyy-MM-dd}");

        var expected = all.EnumerateArray().Count(t =>
        {
            var date = DateOnly.Parse(t.GetProperty("date").GetString()!);
            return date >= from && date <= today;
        });
        Assert.True(expected > 0 && expected < all.GetArrayLength());
        Assert.Equal(expected, week.GetArrayLength());
        Assert.All(week.EnumerateArray(), t => Assert.True(t.GetProperty("checklistItems").GetArrayLength() > 0));
    }

    [Fact]
    public async Task TasksList_RejectsAnInvertedRange()
    {
        var client = await fixture.NewUserClientAsync();

        var response = await client.GetAsync("/api/tasks?from=2026-02-01&to=2026-01-01");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- results must match the previous in-memory computation ----

    [Fact]
    public async Task Dashboard_MatchesAHandComputedResult()
    {
        var client = await fixture.NewUserClientAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var habit = await (await client.PostAsJsonAsync("/api/habits", new
        {
            title = "Run", emoji = "🏃", durationDays = 30, startDate = today.AddDays(-2)
        })).Content.ReadFromJsonAsync<JsonElement>();
        var habitId = habit.GetProperty("id").GetString();
        await client.PostAsJsonAsync("/api/habits", new { title = "Ended", emoji = "x", durationDays = 1, startDate = today.AddDays(-5) });
        foreach (var day in new[] { today.AddDays(-2), today.AddDays(-1), today })
        {
            (await client.PostAsJsonAsync($"/api/habits/{habitId}/check", new { date = day })).EnsureSuccessStatusCode();
        }

        var done = await (await client.PostAsJsonAsync("/api/tasks", new { title = "A", date = today })).Content.ReadFromJsonAsync<JsonElement>();
        await client.PostAsJsonAsync("/api/tasks", new { title = "B", date = today });
        (await client.PostAsJsonAsync($"/api/tasks/{done.GetProperty("id").GetString()}/toggle", new { isCompleted = true })).EnsureSuccessStatusCode();

        var dashboard = await client.GetFromJsonAsync<JsonElement>("/api/dashboard");

        Assert.Equal(2, dashboard.GetProperty("totalHabits").GetInt32());
        Assert.Equal(1, dashboard.GetProperty("activeHabits").GetInt32());
        Assert.Equal(1, dashboard.GetProperty("habitsCompletedToday").GetInt32());
        Assert.Equal(2, dashboard.GetProperty("totalTasks").GetInt32());
        Assert.Equal(1, dashboard.GetProperty("tasksCompletedToday").GetInt32());
        Assert.Equal(1, dashboard.GetProperty("pendingTasks").GetInt32());
        Assert.Equal(3, dashboard.GetProperty("bestHabitStreak").GetInt32());
        Assert.Equal(3, dashboard.GetProperty("currentHabitStreak").GetInt32());

        var analytics = await client.GetFromJsonAsync<JsonElement>($"/api/analytics?from={today.AddDays(-2):yyyy-MM-dd}&to={today:yyyy-MM-dd}");
        Assert.Equal(3, analytics.GetProperty("daily").GetArrayLength());
        Assert.Equal(1, analytics.GetProperty("daily")[2].GetProperty("tasksCompleted").GetInt32());
        Assert.Equal(2, analytics.GetProperty("daily")[2].GetProperty("tasksPlanned").GetInt32());
        Assert.Equal(1, analytics.GetProperty("daily")[2].GetProperty("habitCompletions").GetInt32());
        Assert.Equal(50, analytics.GetProperty("taskCompletionRate").GetDouble());
        Assert.Equal(3, analytics.GetProperty("bestStreak").GetInt32());

        var calendar = await client.GetFromJsonAsync<JsonElement>($"/api/calendar?year={today.Year}&month={today.Month}");
        var todayRow = calendar.GetProperty("days").EnumerateArray().Single(d => d.GetProperty("date").GetString() == today.ToString("yyyy-MM-dd"));
        Assert.Equal(1, todayRow.GetProperty("habitCompletions").GetInt32());
        Assert.Equal(2, todayRow.GetProperty("tasksDue").GetInt32());
        Assert.Equal(1, todayRow.GetProperty("tasksCompleted").GetInt32());
    }

    [Fact]
    public async Task DashboardCache_IsPerClientDay_SoMidnightDoesNotServeYesterday()
    {
        var client = await fixture.NewUserClientAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var habit = await (await client.PostAsJsonAsync("/api/habits", new
        {
            title = "Run", emoji = "🏃", durationDays = 30, startDate = today.AddDays(-1)
        })).Content.ReadFromJsonAsync<JsonElement>();
        (await client.PostAsJsonAsync($"/api/habits/{habit.GetProperty("id").GetString()}/check", new { date = today })).EnsureSuccessStatusCode();

        async Task<JsonElement> DashboardOn(DateOnly clientDay)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard");
            request.Headers.Add("X-Client-Local-Date", clientDay.ToString("yyyy-MM-dd"));
            return await (await client.SendAsync(request)).Content.ReadFromJsonAsync<JsonElement>();
        }

        Assert.Equal(1, (await DashboardOn(today)).GetProperty("habitsCompletedToday").GetInt32());
        Assert.Equal(0, (await DashboardOn(today.AddDays(1))).GetProperty("habitsCompletedToday").GetInt32());
        Assert.Equal(1, (await DashboardOn(today)).GetProperty("habitsCompletedToday").GetInt32());
    }

    // ---- timing benchmark (opt-in: ANITIME_PERF=1) ----

    [PerfFact]
    public async Task Benchmark_HotEndpoints_P50()
    {
        const int runs = 30;
        var (client, userId) = await PerformanceSeed.SeedUserAsync(fixture, SeedSize.Heavy);
        var report = new StringBuilder("| endpoint | queries | p50 ms | bytes | brotli bytes |\n|---|---|---|---|---|\n");

        foreach (var (name, (path, _)) in HotEndpoints)
        {
            var timings = new List<double>();
            var queries = 0;
            long bytes = 0;
            for (var i = 0; i < runs + 3; i++)
            {
                await ClearUserCacheAsync(userId);
                using var scope = QueryCounter.Begin();
                var watch = Stopwatch.StartNew();
                var response = await client.GetAsync(path);
                var body = await response.Content.ReadAsByteArrayAsync();
                watch.Stop();
                response.EnsureSuccessStatusCode();
                if (i >= 3)
                {
                    timings.Add(watch.Elapsed.TotalMilliseconds);
                }

                queries = scope.Count;
                bytes = body.Length;
            }

            timings.Sort();
            var compressedRequest = new HttpRequestMessage(HttpMethod.Get, path);
            compressedRequest.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));
            var compressed = await (await client.SendAsync(compressedRequest)).Content.ReadAsByteArrayAsync();
            report.AppendLine($"| {name} | {queries} | {timings[timings.Count / 2]:F1} | {bytes} | {compressed.Length} |");
        }

        output.WriteLine(report.ToString());
        var outPath = Environment.GetEnvironmentVariable("ANITIME_PERF_OUT");
        if (!string.IsNullOrWhiteSpace(outPath))
        {
            await File.WriteAllTextAsync(outPath, report.ToString());
        }
    }
}

/// <summary>Runs only when ANITIME_PERF=1, because it seeds a heavy dataset and takes a while.</summary>
public sealed class PerfFactAttribute : FactAttribute
{
    public PerfFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ANITIME_PERF") != "1")
        {
            Skip = "Set ANITIME_PERF=1 to run the benchmark.";
        }
    }
}
