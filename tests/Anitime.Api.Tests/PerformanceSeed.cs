using System.Net.Http.Json;
using System.Text.Json;
using Anitime.Domain.Entities;

namespace Anitime.Api.Tests;

public sealed record SeedSize(int Habits, int EntriesPerHabit, int Tasks, int ChecklistPerTask, int Notes, int ParagraphsPerNote, int Reminders)
{
    public static readonly SeedSize Small = new(1, 3, 2, 1, 1, 2, 1);
    public static readonly SeedSize Large = new(12, 200, 120, 3, 12, 20, 15);

    /// <summary>A heavy, realistic year of use, for the timing benchmark.</summary>
    public static readonly SeedSize Heavy = new(15, 365, 3000, 3, 300, 40, 30);
}

/// <summary>Creates a user with data, through the database for speed and through the API for notes (real content shape).</summary>
public static class PerformanceSeed
{
    public static async Task<(HttpClient Client, Guid UserId)> SeedUserAsync(ApiFixture fixture, SeedSize size)
    {
        var client = await fixture.NewUserClientAsync();
        var profile = await client.GetFromJsonAsync<JsonElement>("/api/profile");
        var userId = profile.GetProperty("userId").GetGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var now = DateTime.UtcNow;

        await fixture.WithDbAsync(async db =>
        {
            for (var h = 0; h < size.Habits; h++)
            {
                var habit = new Habit
                {
                    UserId = userId, Title = $"Habit {h}", StartDate = today.AddDays(-(size.EntriesPerHabit - 1)),
                    DurationDays = Math.Max(size.EntriesPerHabit + 30, 30), CreatedAtUtc = now.AddMinutes(-h), UpdatedAtUtc = now
                };
                for (var d = 0; d < size.EntriesPerHabit; d++)
                {
                    habit.Entries.Add(new HabitEntry
                    {
                        Date = habit.StartDate.AddDays(d), IsCompleted = d % 7 != 3, CreatedAtUtc = now, UpdatedAtUtc = now
                    });
                }

                db.Habits.Add(habit);
            }

            for (var t = 0; t < size.Tasks; t++)
            {
                var completed = t % 3 == 0;
                var task = new TaskItem
                {
                    UserId = userId, Title = $"Task {t}", Description = new string('d', 200), Date = today.AddDays(-(t % 365)),
                    IsCompleted = completed, CompletedAtUtc = completed ? now.AddDays(-(t % 365)) : null,
                    CreatedAtUtc = now.AddSeconds(-t), UpdatedAtUtc = now
                };
                for (var c = 0; c < size.ChecklistPerTask; c++)
                {
                    task.ChecklistItems.Add(new ChecklistItem { Title = $"Item {c}", IsDone = c == 0, CreatedAtUtc = now.AddSeconds(c), UpdatedAtUtc = now });
                }

                db.Tasks.Add(task);
            }

            for (var r = 0; r < size.Reminders; r++)
            {
                db.Reminders.Add(new Reminder
                {
                    UserId = userId, Title = $"Reminder {r}", Message = "Drink water", ScheduledForUtc = now.AddHours(r + 1),
                    RepeatDaily = r % 2 == 0, CreatedAtUtc = now, UpdatedAtUtc = now
                });
            }

            await db.SaveChangesAsync();
        });

        for (var n = 0; n < size.Notes; n++)
        {
            var text = string.Join(' ', Enumerable.Repeat("Lorem ipsum dolor sit amet, consectetur adipiscing elit.", 3));
            var blocks = Enumerable.Range(0, size.ParagraphsPerNote).Select(i => new
            {
                type = "paragraph", align = "left",
                children = new[] { new { textNode = new { type = "text", text = $"{i}: {text}" } } }
            }).ToArray();
            var response = await client.PostAsJsonAsync("/api/notes", new
            {
                title = $"Note {n}", contentJson = new { version = 1, blocks }, plainTextPreview = text,
                fontFamily = "system-ui", fontSize = 16, fontWeight = 400, textColor = "#111827", backgroundColor = "#FFFFFF",
                isPinned = n % 10 == 0, isArchived = false
            });
            response.EnsureSuccessStatusCode();
        }

        return (client, userId);
    }
}
