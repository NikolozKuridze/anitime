namespace Anitime.Contracts;

/// <summary>Adds a checklist item (POST) or renames one (PUT /api/tasks/{taskId}/checklist/{itemId}).</summary>
public sealed record ChecklistItemRequest(string Title);

public sealed record CreateTaskRequest(
    string Title,
    string? Description,
    string? Emoji,
    DateOnly Date,
    IReadOnlyCollection<ChecklistItemRequest>? ChecklistItems,
    string? ClientRequestId = null,
    string? Priority = null);

/// <param name="Priority">Low, Medium or High (any case). Omitted or null keeps the stored value (installed app builds don't send it).</param>
public sealed record UpdateTaskRequest(string Title, string? Description, string? Emoji, DateOnly Date, string? Priority = null);

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
    IReadOnlyCollection<ChecklistItemResponse> ChecklistItems,
    string Priority,
    string PriorityLabel);
