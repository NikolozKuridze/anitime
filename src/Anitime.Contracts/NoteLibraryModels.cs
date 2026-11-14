namespace Anitime.Contracts;

// ---- folders ----

/// <param name="ParentId">Null for a top-level folder. The parent must itself be top-level (two levels at most).</param>
public sealed record CreateNoteFolderRequest(string Name, Guid? ParentId = null);

/// <param name="ParentId">The new parent, or null to make it top-level.</param>
public sealed record UpdateNoteFolderRequest(string Name, Guid? ParentId = null);

/// <param name="NoteCount">Notes in this folder that are not archived or deleted (subfolders not included).</param>
public sealed record NoteFolderResponse(Guid Id, string Name, Guid? ParentId, int NoteCount, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

/// <param name="FolderId">Null moves the notes out of any folder.</param>
public sealed record MoveNotesRequest(IReadOnlyList<Guid> NoteIds, Guid? FolderId);

/// <param name="Moved">How many of the given notes were found and moved; ids of other users' or deleted notes are skipped.</param>
public sealed record MoveNotesResponse(int Moved);

// ---- tags ----

/// <param name="NoteCount">Notes carrying the tag, archived included, Recently Deleted excluded.</param>
public sealed record NoteTagResponse(string Name, int NoteCount);

// ---- smart folders ----

/// <summary>Every set field must match (AND). Dates are UTC days, inclusive.</summary>
/// <param name="TagMode">"any" (default) or "all" of <paramref name="Tags"/>.</param>
public sealed record SmartFolderFilter(
    IReadOnlyList<string>? Tags = null,
    string? TagMode = null,
    bool? HasChecklist = null,
    bool? HasImage = null,
    bool? IsPinned = null,
    bool? IsLocked = null,
    DateOnly? CreatedFrom = null,
    DateOnly? CreatedTo = null,
    DateOnly? EditedFrom = null,
    DateOnly? EditedTo = null);

public sealed record SaveSmartFolderRequest(string Name, SmartFolderFilter Filter);

public sealed record SmartFolderResponse(Guid Id, string Name, SmartFolderFilter Filter, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

// ---- Recently Deleted and edit-clash copies ----

/// <param name="PurgeAtUtc">When the note is deleted forever (30 days after deletion).</param>
public sealed record DeletedNoteResponse(
    Guid Id,
    string Title,
    string PlainTextPreview,
    Guid? FolderId,
    bool IsLocked,
    DateTime DeletedAtUtc,
    DateTime PurgeAtUtc,
    DateTime UpdatedAtUtc);

/// <summary>The version the user did NOT keep after a 409; it is saved as a new note straight into Recently Deleted.</summary>
/// <param name="Title">The title of that version; defaults to the stored note's title. The copy is named "Copy of {title}".</param>
public sealed record ConflictCopyRequest(RichNoteDocument ContentJson, string? PlainTextPreview = null, string? Title = null);

// ---- attachments ----

public sealed record NoteAttachmentResponse(Guid AssetId, string ContentType, long SizeBytes, DateTime CreatedAtUtc);

/// <param name="Url">Absolute, works without a bearer token (for the editor WebView) until <paramref name="ExpiresAtUtc"/>.</param>
public sealed record AttachmentUrlResponse(string Url, DateTime ExpiresAtUtc);

// ---- search and backlinks ----

/// <summary>A match inside a string, in UTF-16 code units (JavaScript string indices).</summary>
public sealed record TextRange(int Start, int Length);

/// <param name="Snippet">Text around the first body match; empty for locked notes, which match by title only.</param>
public sealed record NoteSearchResult(
    Guid Id,
    string Title,
    IReadOnlyList<TextRange> TitleRanges,
    string Snippet,
    IReadOnlyList<TextRange> SnippetRanges,
    Guid? FolderId,
    bool IsPinned,
    bool IsArchived,
    bool IsLocked,
    DateTime UpdatedAtUtc);

public sealed record NoteLinkResponse(Guid Id, string Title, DateTime UpdatedAtUtc);
