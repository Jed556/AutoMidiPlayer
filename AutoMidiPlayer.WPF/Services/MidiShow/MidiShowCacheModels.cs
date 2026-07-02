using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoMidiPlayer.WPF.Services.MidiShow;

/// <summary>
/// JSON-serializable wrapper that stores cached data alongside a timestamp for TTL checks.
/// </summary>
/// <typeparam name="T">The cached payload type.</typeparam>
public sealed class CacheEntry<T>
{
    public T? Data { get; set; }
    public DateTime CachedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// JSON-serializable mirror of <see cref="MidiShowItem"/> for disk caching.
/// Uses plain auto-properties so System.Text.Json can round-trip them without
/// pulling in Stylet or WPF dependencies.
/// </summary>
public sealed class CachedMidiShowItem
{
    public string Id { get; set; } = string.Empty;
    public string PageUrl { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Uploader { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? Standard { get; set; }
    public string Duration { get; set; } = "";
    public string Downloads { get; set; } = "0";
    public string TrackCount { get; set; } = "0";
    public string Category { get; set; } = "";
    public string Tags { get; set; } = "";
    public List<string> TagsList { get; set; } = new();
    public string Description { get; set; } = "";
    public string Rating { get; set; } = "0.0";
    public string RatingCount { get; set; } = "0";
    public string? InstrumentCount { get; set; }
    public string? FileSize { get; set; }
    public string? UploadDate { get; set; }

    /// <summary>Converts from the live view model to a cache-safe DTO.</summary>
    public static CachedMidiShowItem From(MidiShowItem item) => new()
    {
        Id = item.Id,
        PageUrl = item.PageUrl,
        Title = item.Title,
        Uploader = item.Uploader,
        ThumbnailUrl = item.ThumbnailUrl,
        Standard = item.Standard,
        Duration = item.Duration,
        Downloads = item.Downloads,
        TrackCount = item.TrackCount,
        Category = item.Category,
        Tags = item.Tags,
        TagsList = new List<string>(item.TagsList),
        Description = item.Description,
        Rating = item.Rating,
        RatingCount = item.RatingCount,
        InstrumentCount = item.InstrumentCount,
        FileSize = item.FileSize,
        UploadDate = item.UploadDate
    };

    /// <summary>Reconstitutes a live <see cref="MidiShowItem"/> from the cached DTO.</summary>
    public MidiShowItem ToItem() => new()
    {
        Id = Id,
        PageUrl = PageUrl,
        Title = Title,
        Uploader = Uploader,
        ThumbnailUrl = ThumbnailUrl,
        Standard = Standard,
        Duration = Duration,
        Downloads = Downloads,
        TrackCount = TrackCount,
        Category = Category,
        Tags = Tags,
        TagsList = TagsList,
        Description = Description,
        Rating = Rating,
        RatingCount = RatingCount,
        InstrumentCount = InstrumentCount,
        FileSize = FileSize,
        UploadDate = UploadDate
    };
}

/// <summary>
/// JSON-serializable mirror of <see cref="MidiShowDetails"/> for disk caching.
/// </summary>
public sealed class CachedMidiShowDetails
{
    public string Id { get; set; } = string.Empty;
    public string PageUrl { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Uploader { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? Standard { get; set; }
    public string? Duration { get; set; }
    public string? Bpm { get; set; }
    public string? TrackCount { get; set; }
    public string? NoteCount { get; set; }
    public string? FileSize { get; set; }
    public string? Instruments { get; set; }
    public string? Rating { get; set; }
    public string? Introduction { get; set; }
    public string Category { get; set; } = "";
    public string Tags { get; set; } = "";
    public string Downloads { get; set; } = "0";
    public List<CachedMidiShowTrack> Tracks { get; set; } = new();

    public static CachedMidiShowDetails From(MidiShowDetails details) => new()
    {
        Id = details.Id,
        PageUrl = details.PageUrl,
        Title = details.Title,
        Uploader = details.Uploader,
        ThumbnailUrl = details.ThumbnailUrl,
        Standard = details.Standard,
        Duration = details.Duration,
        Bpm = details.Bpm,
        TrackCount = details.TrackCount,
        NoteCount = details.NoteCount,
        FileSize = details.FileSize,
        Instruments = details.Instruments,
        Rating = details.Rating,
        Introduction = details.Introduction,
        Category = details.Category,
        Tags = details.Tags,
        Downloads = details.Downloads,
        Tracks = details.Tracks is { Count: > 0 }
            ? details.Tracks.Select(t => new CachedMidiShowTrack
            {
                Number = t.Number,
                Name = t.Name,
                Channel = t.Channel,
                Instrument = t.Instrument,
                ProgramId = t.ProgramId,
                NotesCount = t.NotesCount
            }).ToList()
            : new()
    };

    public MidiShowDetails ToDetails() => new()
    {
        Id = Id,
        PageUrl = PageUrl,
        Title = Title,
        Uploader = Uploader,
        ThumbnailUrl = ThumbnailUrl,
        Standard = Standard,
        Duration = Duration,
        Bpm = Bpm,
        TrackCount = TrackCount,
        NoteCount = NoteCount,
        FileSize = FileSize,
        Instruments = Instruments,
        Rating = Rating,
        Introduction = Introduction,
        Category = Category,
        Tags = Tags,
        Downloads = Downloads,
        Tracks = Tracks.Select(t => new MidiShowTrack
        {
            Number = t.Number,
            Name = t.Name,
            Channel = t.Channel,
            Instrument = t.Instrument,
            ProgramId = t.ProgramId,
            NotesCount = t.NotesCount
        }).ToList()
    };
}

/// <summary>JSON-serializable mirror of <see cref="MidiShowTrack"/>.</summary>
public sealed class CachedMidiShowTrack
{
    public string Number { get; set; } = "";
    public string Name { get; set; } = "";
    public string Channel { get; set; } = "";
    public string Instrument { get; set; } = "";
    public string ProgramId { get; set; } = "";
    public string NotesCount { get; set; } = "";
}
