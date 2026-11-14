using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Anitime.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Anitime.Api.Tests;

/// <summary>
/// Search on a heavy account: 300 long notes (~13 KB of text each) and 3,000 short ones, next to another user with
/// 2,000 notes that must never be scanned for this user's searches. No database extension is involved.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class NoteSearchPerformanceTests(ApiFixture fixture, ITestOutputHelper output)
{
    private const int LongNotes = 300;
    private const int LongNoteChars = 13_000;
    private const int ShortNotes = 3_000;
    private const int NeighbourNotes = 2_000;
    private const int Runs = 25;
    private const double MaxP50Milliseconds = 50;

    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static HttpClient? _client;

    private static readonly string[] Words =
    [
        "plan", "meeting", "budget", "groceries", "habit", "workout", "notes", "travel", "ideas", "project",
        "მაწონი", "პური", "შეხვედრა", "გეგმა", "ბიუჯეტი", "მოგზაურობა", "იდეა", "პროექტი", "სამუშაო", "წიგნი"
    ];

    // name → (query, expected hits): a miss that scans everything, common English/Georgian words, a rare word, two words.
    public static TheoryData<string> Queries() => new() { "zzqxv", "budget", "მაწონ", "unicornfield", "plan შეხვედრა" };

    private async Task<HttpClient> HeavyClientAsync()
    {
        await SeedLock.WaitAsync();
        try
        {
            if (_client is not null)
            {
                return _client;
            }

            var client = await fixture.NewUserClientAsync();
            var neighbour = await fixture.NewUserClientAsync();
            var userId = await UserIdAsync(client);
            var neighbourId = await UserIdAsync(neighbour);
            var random = new Random(42);
            var now = DateTime.UtcNow;

            await fixture.WithDbAsync(async db =>
            {
                for (var i = 0; i < LongNotes; i++)
                {
                    var text = LongText(random, i % 50 == 0 ? "unicornfield" : null);
                    db.Notes.Add(NoteOf(userId, $"Long note {i}", text, now.AddMinutes(-i)));
                }

                for (var i = 0; i < ShortNotes; i++)
                {
                    db.Notes.Add(NoteOf(userId, $"Short {i}", $"{Words[random.Next(Words.Length)]} {Words[random.Next(Words.Length)]} item {i}", now.AddMinutes(-i)));
                }

                for (var i = 0; i < NeighbourNotes; i++)
                {
                    db.Notes.Add(NoteOf(neighbourId, $"Neighbour {i}", LongText(random, "unicornfield")[..2_000], now));
                }

                await db.SaveChangesAsync();
            });
            await fixture.WithDbAsync(db => db.Database.ExecuteSqlRawAsync("ANALYZE notes"));

            return _client = client;
        }
        finally
        {
            SeedLock.Release();
        }
    }

    [Theory]
    [MemberData(nameof(Queries))]
    public async Task Search_OnHeavyAccount_P50StaysUnder50ms_InOneQuery(string q)
    {
        var client = await HeavyClientAsync();
        var path = $"/api/notes/search?q={Uri.EscapeDataString(q)}";
        for (var i = 0; i < 3; i++)
        {
            (await client.GetAsync(path)).EnsureSuccessStatusCode(); // warm up
        }

        var timings = new List<double>();
        var hits = 0;
        for (var i = 0; i < Runs; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            var response = await client.GetAsync(path);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            stopwatch.Stop();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            hits = body.GetArrayLength();
            timings.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        using var scope = QueryCounter.Begin();
        (await client.GetAsync(path)).EnsureSuccessStatusCode();
        scope.Dispose();

        timings.Sort();
        var p50 = timings[timings.Count / 2];
        output.WriteLine($"search \"{q}\": p50 {p50:F1} ms, p90 {timings[(int)(timings.Count * 0.9)]:F1} ms, {hits} hits, {scope.Count} queries");
        Assert.Equal(1, scope.Count);
        Assert.True(p50 < MaxP50Milliseconds, $"search \"{q}\" p50 {p50:F1} ms (target < {MaxP50Milliseconds} ms)");
    }

    [Fact]
    public async Task Search_NeverReturnsTheNeighboursNotes()
    {
        var client = await HeavyClientAsync();

        var hits = await client.GetFromJsonAsync<JsonElement>("/api/notes/search?q=unicornfield&limit=100");

        Assert.Equal(LongNotes / 50, hits.GetArrayLength());
        Assert.All(hits.EnumerateArray(), hit => Assert.StartsWith("Long note", hit.GetProperty("title").GetString()));
    }

    [Fact]
    public async Task SearchPlan_NarrowsToTheUserThroughTheBtreeIndex_AndSearchTextIsLz4()
    {
        var client = await HeavyClientAsync();
        var userId = await UserIdAsync(client);
        var plan = new List<string>();
        var compression = "";

        await fixture.WithDbAsync(async db =>
        {
            var connection = db.Database.GetDbConnection();
            await connection.OpenAsync();
            await using var explain = connection.CreateCommand();
            // Same shape as the SQL EF generates for GET /api/notes/search (checked in PerformanceApiTests).
            explain.CommandText = "EXPLAIN SELECT n.\"Id\" FROM notes n WHERE NOT n.\"IsDeleted\" AND n.\"UserId\" = '" + userId +
                "' AND strpos(n.\"SearchText\", 'zzqxv') - 1 >= 0 ORDER BY n.\"UpdatedAtUtc\" DESC LIMIT 50";
            await using (var reader = await explain.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    plan.Add(reader.GetString(0));
                }
            }

            await using var column = connection.CreateCommand();
            column.CommandText = "SELECT attcompression::text FROM pg_attribute WHERE attrelid = 'notes'::regclass AND attname = 'SearchText'";
            compression = (string)(await column.ExecuteScalarAsync())!;
        });

        output.WriteLine(string.Join("\n", plan));
        Assert.Contains(plan, line => line.Contains("IX_notes_UserId_IsDeleted_UpdatedAtUtc", StringComparison.Ordinal));
        Assert.DoesNotContain(plan, line => line.Contains("Seq Scan", StringComparison.Ordinal));
        Assert.Equal("l", compression);
    }

    private static async Task<Guid> UserIdAsync(HttpClient client) =>
        Guid.Parse((await client.GetFromJsonAsync<JsonElement>("/api/profile")).GetProperty("userId").GetString()!);

    private static string LongText(Random random, string? marker)
    {
        var builder = new StringBuilder(LongNoteChars + 100);
        var line = 0;
        while (builder.Length < LongNoteChars)
        {
            builder.Append(Words[random.Next(Words.Length)]).Append(' ');
            if (++line % 12 == 0)
            {
                builder.Append('\n');
            }
        }

        if (marker is not null)
        {
            builder.Insert(LongNoteChars / 2, $" {marker} ");
        }

        return builder.ToString();
    }

    private static Note NoteOf(Guid userId, string title, string text, DateTime at) => new()
    {
        UserId = userId,
        Title = title,
        ContentJson = JsonSerializer.Serialize(new { Version = 1, Blocks = Array.Empty<object>() }),
        PlainTextPreview = text,
        VersionToken = Guid.NewGuid().ToString("N"),
        LastEditedAtUtc = at,
        CreatedAtUtc = at,
        UpdatedAtUtc = at
    };
}
