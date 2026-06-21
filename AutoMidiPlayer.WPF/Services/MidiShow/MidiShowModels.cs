namespace AutoMidiPlayer.WPF.Services.MidiShow;

/// <summary>
/// A single MIDI entry parsed from a MidiShow list or search results page.
/// </summary>
public sealed class MidiShowItem
{
    /// <summary>Numeric MidiShow id (from the <c>data-key</c> attribute).</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Absolute URL of the MIDI detail page (used to download).</summary>
    public string PageUrl { get; init; } = string.Empty;

    /// <summary>Display title of the track.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Uploader / author username, when available.</summary>
    public string? Uploader { get; init; }

    /// <summary>Avatar/thumbnail image URL, when available.</summary>
    public string? ThumbnailUrl { get; init; }

    /// <summary>MIDI standard label (e.g. "GM1"), when available.</summary>
    public string? Standard { get; init; }

    /// <summary>Track duration, e.g. "02:55" (string, "" when unknown).</summary>
    public string Duration { get; init; } = "";

    /// <summary>Download count, e.g. "720" (number as string, "0" when unknown).</summary>
    public string Downloads { get; init; } = "0";

    /// <summary>Number of tracks ("0" when unknown).</summary>
    public string TrackCount { get; init; } = "0";

    /// <summary>Music category, e.g. "Anime/Game music" ("" when unknown).</summary>
    public string Category { get; init; } = "";

    /// <summary>Tags joined for display, e.g. "your name · sparkle" ("" when none).</summary>
    public string Tags { get; init; } = "";

    /// <summary>Short description / introduction snippet ("" when none).</summary>
    public string Description { get; init; } = "";

    /// <summary>Average rating, e.g. "5.0" ("0.0" when unrated).</summary>
    public string Rating { get; init; } = "0.0";

    /// <summary>Number of ratings ("0" when none).</summary>
    public string RatingCount { get; init; } = "0";

    public bool HasStandard => !string.IsNullOrEmpty(Standard);
    public bool HasThumbnail => !string.IsNullOrEmpty(ThumbnailUrl);
    public bool HasDuration => !string.IsNullOrEmpty(Duration);
    public bool HasDownloads => Downloads is not (null or "" or "0");
    public bool HasTrackCount => TrackCount is not (null or "" or "0");
    public bool HasCategory => !string.IsNullOrEmpty(Category);
    public bool HasTags => !string.IsNullOrEmpty(Tags);
    public bool HasDescription => !string.IsNullOrEmpty(Description);
    public bool HasRating => RatingCount is not (null or "" or "0");

    /// <summary>Rating shown as "5.0 (8)".</summary>
    public string RatingDisplay => $"{Rating} ({RatingCount})";
}

/// <summary>
/// Full details for a single MIDI, parsed from its MidiShow detail page on demand.
/// </summary>
public sealed class MidiShowDetails
{
    public string Id { get; init; } = string.Empty;
    public string PageUrl { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Uploader { get; init; }
    public string? ThumbnailUrl { get; init; }
    public string? Standard { get; init; }

    public string? Duration { get; init; }
    public string? Bpm { get; init; }
    public string? TrackCount { get; init; }
    public string? NoteCount { get; init; }
    public string? FileSize { get; init; }
    public string? Instruments { get; init; }
    public string? Rating { get; init; }
    public string? Introduction { get; init; }
    public string Category { get; init; } = "";
    public string Tags { get; init; } = "";
    public string Downloads { get; init; } = "0";

    public bool HasThumbnail => !string.IsNullOrEmpty(ThumbnailUrl);
    public bool HasStandard => !string.IsNullOrEmpty(Standard);
    public bool HasDuration => !string.IsNullOrEmpty(Duration);
    public bool HasBpm => Bpm is not (null or "" or "0");
    public bool HasTrackCount => TrackCount is not (null or "" or "0");
    public bool HasNoteCount => NoteCount is not (null or "" or "0");
    public bool HasFileSize => !string.IsNullOrEmpty(FileSize);
    public bool HasInstruments => !string.IsNullOrEmpty(Instruments);
    public bool HasRating => !string.IsNullOrEmpty(Rating);
    public bool HasIntroduction => !string.IsNullOrEmpty(Introduction);
    public bool HasCategory => !string.IsNullOrEmpty(Category);
    public bool HasTags => !string.IsNullOrEmpty(Tags);
    public bool HasDownloads => Downloads is not (null or "" or "0");
}

/// <summary>
/// Result of a download attempt: the raw MIDI bytes and a suggested title.
/// </summary>
public sealed record MidiShowDownloadResult(byte[] Data, string Title, System.Collections.Generic.Dictionary<int, string>? TrackNames = null);

/// <summary>
/// Reasons a download may fail, surfaced to the UI for a friendly message.
/// </summary>
public enum MidiShowDownloadError
{
    None,
    NotAuthenticated,
    NotFound,
    Network,
    Decode
}

/// <summary>
/// Thrown by <see cref="MidiShowClient"/> when an operation cannot complete.
/// </summary>
public sealed class MidiShowException : System.Exception
{
    public MidiShowDownloadError Reason { get; }

    public MidiShowException(MidiShowDownloadError reason, string message, System.Exception? inner = null)
        : base(message, inner)
    {
        Reason = reason;
    }
}
