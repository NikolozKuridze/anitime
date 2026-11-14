namespace Anitime.Domain.Entities;

public sealed class Note : BaseEntity
{
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ContentJson { get; set; } = "{\"version\":1,\"blocks\":[]}";
    public string PlainTextPreview { get; set; } = string.Empty;
    public string FontFamily { get; set; } = "system-ui";
    public int FontSize { get; set; } = 16;
    public int FontWeight { get; set; } = 400;
    public string TextColor { get; set; } = "#111827";
    public string BackgroundColor { get; set; } = "#FFFFFF";
    public bool IsPinned { get; set; }
    public bool IsArchived { get; set; }
    public string VersionToken { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime? LastEditedAtUtc { get; set; }

    /// <summary>Null means "no folder". Moving a note doesn't rotate <see cref="VersionToken"/>.</summary>
    public Guid? FolderId { get; set; }

    /// <summary>Hides the note behind Face ID in the app; the content is not encrypted and still syncs.</summary>
    public bool IsLocked { get; set; }

    public string PaperStyle { get; set; } = NotePaperStyles.Plain;

    /// <summary>Tags the user typed as <c>#tag</c> in the content plus <see cref="ExplicitTags"/>, normalized and sorted.</summary>
    public List<string> Tags { get; set; } = [];

    public List<string> ExplicitTags { get; set; } = [];

    /// <summary>Ids from <c>anitime-note:</c> links in the content (for backlinks).</summary>
    public List<Guid> LinkedNoteIds { get; set; } = [];

    /// <summary>Attachment ids used by image nodes (for orphan cleanup).</summary>
    public List<Guid> AssetIds { get; set; } = [];

    public bool HasChecklist { get; set; }
    public bool HasImage { get; set; }

    /// <summary>
    /// Lowercased title and preview, which search matches with a plain LIKE (no database extension needed).
    /// A locked note holds only its title, so its text never shows up in search. Kept current by AppDbContext on save.
    /// </summary>
    public string SearchText { get; set; } = string.Empty;
    public User User { get; set; } = default!;

    public static string BuildSearchText(string title, string plainTextPreview, bool isLocked) =>
        (isLocked ? title : title + "\n" + plainTextPreview).ToLowerInvariant();
}

public static class NotePaperStyles
{
    public const string Plain = "plain";
    public const string Lines = "lines";
    public const string Grid = "grid";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { Plain, Lines, Grid };
}
