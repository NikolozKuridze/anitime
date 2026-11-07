using Anitime.Application.Rules;
using Anitime.Contracts;
using Anitime.Domain.Entities;
using Microsoft.AspNetCore.Http;

namespace Anitime.Api.Mapping;

public static class ApiMappings
{
    public static HabitResponse ToResponse(this Habit habit, DateOnly today)
    {
        var streaks = HabitProgressCalculator.CalculateStreaks(habit, today);
        return new HabitResponse(
            habit.Id,
            habit.Title,
            habit.Emoji,
            habit.StartDate,
            habit.DurationDays,
            streaks.Current,
            streaks.Best,
            habit.Entries.Count(x => x.IsCompleted),
            habit.Entries
                .OrderBy(x => x.Date)
                .Select(x => new HabitEntryResponse(x.Id, x.Date, x.IsCompleted))
                .ToList());
    }

    public static TaskResponse ToResponse(this TaskItem task) =>
        new(
            task.Id,
            task.Title,
            task.Description,
            task.Emoji,
            task.Date,
            task.IsCompleted,
            task.ChecklistItems
                .OrderBy(x => x.CreatedAtUtc)
                .Select(x => new ChecklistItemResponse(x.Id, x.Title, x.IsDone))
                .ToList());

    public static ReminderResponse ToResponse(this Reminder reminder) =>
        new(
            reminder.Id,
            reminder.Title,
            reminder.Message,
            reminder.ScheduledForUtc,
            reminder.RepeatDaily,
            reminder.IsEnabled,
            reminder.LastTriggeredAtUtc);

    public static UserProfileResponse ToResponse(this User user, HttpRequest request)
    {
        var settings = user.Settings ?? new UserSettings();
        return new UserProfileResponse(
            user.Id,
            user.Email,
            user.Name,
            user.Gender,
            user.DateOfBirth,
            CalculateAge(user.DateOfBirth),
            string.IsNullOrWhiteSpace(user.ProfileImageObjectName) ? null : BuildAbsoluteApiUrl(request, "/api/profile/profile-image"),
            string.IsNullOrWhiteSpace(user.BackgroundImageObjectName) ? null : BuildAbsoluteApiUrl(request, "/api/profile/background-image"),
            new UserSettingsResponse(
                settings.BackgroundStyleType,
                settings.BackgroundPrimaryColor,
                settings.BackgroundSecondaryColor,
                settings.AccentColor,
                settings.Locale,
                settings.TimeZone));
    }

    public static ExerciseSummaryResponse ToSummaryResponse(this Exercise exercise, string imageBaseUrl) =>
        new(
            exercise.Id,
            exercise.Slug,
            exercise.Name,
            exercise.Category,
            exercise.Level,
            exercise.Equipment,
            exercise.PrimaryMuscles,
            exercise.ImagePaths.Count > 0 ? BuildImageUrl(imageBaseUrl, exercise.ImagePaths[0]) : null);

    public static ExerciseDetailResponse ToDetailResponse(this Exercise exercise, string imageBaseUrl) =>
        new(
            exercise.Id,
            exercise.Slug,
            exercise.Name,
            exercise.Category,
            exercise.Level,
            exercise.Force,
            exercise.Mechanic,
            exercise.Equipment,
            exercise.PrimaryMuscles,
            exercise.SecondaryMuscles,
            exercise.Instructions,
            exercise.ImagePaths.Select(path => BuildImageUrl(imageBaseUrl, path)).ToList(),
            string.IsNullOrWhiteSpace(exercise.YouTubeVideoId) ? null : BuildEmbedUrl(exercise.YouTubeVideoId));

    public static WorkoutSummaryResponse ToSummaryResponse(this Workout workout) =>
        new(
            workout.Id,
            workout.Slug,
            workout.Title,
            workout.Description,
            workout.Category,
            workout.Level,
            workout.DurationMinutes,
            workout.YouTubeVideoId,
            BuildEmbedUrl(workout.YouTubeVideoId),
            BuildThumbnailUrl(workout.YouTubeVideoId),
            workout.TargetMuscles);

    public static WorkoutDetailResponse ToDetailResponse(
        this Workout workout,
        IReadOnlyList<ExerciseSummaryResponse> exercises) =>
        new(
            workout.Id,
            workout.Slug,
            workout.Title,
            workout.Description,
            workout.Category,
            workout.Level,
            workout.DurationMinutes,
            workout.YouTubeVideoId,
            BuildEmbedUrl(workout.YouTubeVideoId),
            BuildThumbnailUrl(workout.YouTubeVideoId),
            workout.TargetMuscles,
            exercises);

    private static string BuildImageUrl(string baseUrl, string relativePath) =>
        $"{baseUrl.TrimEnd('/')}/{relativePath.TrimStart('/')}";

    private static string BuildEmbedUrl(string youTubeVideoId) =>
        $"https://www.youtube.com/embed/{youTubeVideoId}";

    private static string BuildThumbnailUrl(string youTubeVideoId) =>
        $"https://img.youtube.com/vi/{youTubeVideoId}/hqdefault.jpg";

    public static string BuildObjectName(Guid userId, string assetType, string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return $"users/{userId}/{assetType}/{Guid.NewGuid():N}{extension}";
    }

    public static string? ValidateImageFile(IFormFile file)
    {
        if (file.Length == 0)
        {
            return "Image file is empty.";
        }

        if (file.Length > 5 * 1024 * 1024)
        {
            return "Image file must be 5 MB or smaller.";
        }

        var allowed = new[] { "image/jpeg", "image/png", "image/webp" };
        if (!allowed.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            return "Only JPEG, PNG, and WEBP images are supported.";
        }

        return null;
    }

    public static int? CalculateAge(DateOnly? dateOfBirth)
    {
        if (dateOfBirth is null)
        {
            return null;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var age = today.Year - dateOfBirth.Value.Year;
        if (dateOfBirth.Value > today.AddYears(-age))
        {
            age--;
        }

        return age;
    }

    private static string BuildAbsoluteApiUrl(HttpRequest request, string path) => $"{request.Scheme}://{request.Host}{path}";
}
