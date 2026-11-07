namespace Anitime.Contracts;

public sealed record ChecklistItemRequest(string Title);

public sealed record CreateTaskRequest(
    string Title,
    string? Description,
    string? Emoji,
    DateOnly Date,
    IReadOnlyCollection<ChecklistItemRequest>? ChecklistItems,
    string? ClientRequestId = null);

public sealed record UpdateTaskRequest(string Title, string? Description, string? Emoji, DateOnly Date);

public sealed record ToggleTaskCompletionRequest(bool? IsCompleted);

public sealed record ChecklistItemResponse(Guid Id, string Title, bool IsDone);

public sealed record DeleteTaskResponse(Guid Id);

public sealed record TaskResponse(
    Guid Id,
    string Title,
    string? Description,
    string Emoji,
    DateOnly Date,
    bool IsCompleted,
    IReadOnlyCollection<ChecklistItemResponse> ChecklistItems);
