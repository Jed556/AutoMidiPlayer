using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.Data.Entities;
using AutoMidiPlayer.WPF.ModernWPF;
using Stylet;
using StyletIoC;
using static AutoMidiPlayer.Data.Entities.Transpose;
using MidiFile = AutoMidiPlayer.Data.Midi.MidiFile;

namespace AutoMidiPlayer.WPF.Services;

/// <summary>
/// Service responsible for per-song settings (key, speed, transpose).
/// Isolated from playback controls so non-playback consumers (e.g. edit dialog, track stats)
/// can read/write song settings without depending on PlaybackService.
/// </summary>
public class SongSettingsService : PropertyChangedBase
{
    private readonly IContainer _ioc;
    private readonly IEventAggregator _events;

    private int _keyOffset;
    private double _speed = 1.0;
    private MusicConstants.KeyOption? _selectedKeyOption;
    private MusicConstants.SpeedOption? _selectedSpeedOption;

    public SongSettingsService(IContainer ioc)
    {
        _ioc = ioc;
        _events = ioc.Get<IEventAggregator>();
    }

    #region Static Data

    public static Dictionary<Transpose, string> TransposeNames => MusicConstants.TransposeNames;

    public Dictionary<int, string> KeyOffsets => MusicConstants.KeyOffsets;

    #endregion

    #region Options (for ComboBox binding)

    public List<MusicConstants.KeyOption> KeyOptions { get; } = MusicConstants.GenerateKeyOptions();

    public List<MusicConstants.SpeedOption> SpeedOptions { get; } = MusicConstants.GenerateSpeedOptions();

    #endregion

    #region Current File

    /// <summary>
    /// The currently loaded file. Set by PlaybackService when a new song is opened.
    /// </summary>
    public MidiFile? CurrentFile { get; set; }

    #endregion

    #region Properties

    public int KeyOffset
    {
        get => _keyOffset;
        set
        {
            if (SetAndNotify(ref _keyOffset, Math.Clamp(value,
                    MusicConstants.MinKeyOffset, MusicConstants.MaxKeyOffset)))
            {
                _selectedKeyOption = KeyOptions.FirstOrDefault(k => k.Value == _keyOffset);
                NotifyOfPropertyChange(nameof(SelectedKeyOption));

                // Persist + notify for playback rebuild
                SaveCurrentSongKey();
            }
        }
    }

    public MusicConstants.KeyOption? SelectedKeyOption
    {
        get => _selectedKeyOption ??= KeyOptions.FirstOrDefault(k => k.Value == KeyOffset);
        set
        {
            if (value != null && SetAndNotify(ref _selectedKeyOption, value))
                KeyOffset = value.Value;
        }
    }

    public double Speed
    {
        get => _speed;
        set
        {
            if (SetAndNotify(ref _speed, Math.Round(Math.Clamp(value, 0.1, 4.0), 1)))
            {
                _selectedSpeedOption = SpeedOptions.FirstOrDefault(s => Math.Abs(s.Value - _speed) < 0.01)
                    ?? SpeedOptions.First(s => s.Value == 1.0);
                NotifyOfPropertyChange(nameof(SelectedSpeedOption));

                // Notify PlaybackService to update live playback speed
                SpeedChanged?.Invoke(_speed);

                // Persist to current song
                SaveCurrentSongSpeed();
            }
        }
    }

    public MusicConstants.SpeedOption? SelectedSpeedOption
    {
        get => _selectedSpeedOption ??= SpeedOptions.FirstOrDefault(s => Math.Abs(s.Value - Speed) < 0.01)
            ?? SpeedOptions.First(s => s.Value == 1.0);
        set
        {
            if (value != null && SetAndNotify(ref _selectedSpeedOption, value))
                Speed = value.Value;
        }
    }

    public KeyValuePair<Transpose, string>? Transpose { get; set; }

    #endregion

    #region Events

    /// <summary>
    /// Fired when speed changes so PlaybackService can update Playback.Speed
    /// without a full rebuild.
    /// </summary>
    public event Action<double>? SpeedChanged;

    /// <summary>
    /// Fired when key offset or transpose changes, requiring a playback rebuild.
    /// </summary>
    public event Action? SettingsRebuildRequired;

    #endregion

    #region Methods

    public void IncreaseSpeed() => Speed = Math.Round(Speed + 0.1, 1);

    public void DecreaseSpeed() => Speed = Math.Round(Speed - 0.1, 1);

    /// <summary>
    /// Apply per-song settings (key, speed, transpose) when a new song is loaded.
    /// </summary>
    public void ApplyPerSongSettings(MidiFile file)
    {
        CurrentFile = file;

        // Speed: per-song or default 1.0
        Speed = file.Song.Speed ?? 1.0;

        // Key offset: always from song
        KeyOffset = file.Song.Key;

        // Transpose: from song or null
        var transpose = TransposeNames
            .FirstOrDefault(e => e.Key == file.Song.Transpose);
        Transpose = file.Song.Transpose is not null ? transpose : null;
    }

    /// <summary>
    /// Clear settings when file is closed.
    /// </summary>
    public void ClearSettings()
    {
        CurrentFile = null;
        Transpose = null;
    }

    #endregion

    #region Persistence

    private async void SaveCurrentSongKey()
    {
        if (CurrentFile is null) return;
        CurrentFile.Song.Key = KeyOffset;
        await SaveSongAsync(CurrentFile.Song);

        // Key change requires playback rebuild
        SettingsRebuildRequired?.Invoke();
    }

    private async void SaveCurrentSongSpeed()
    {
        if (CurrentFile is null) return;
        CurrentFile.Song.Speed = _speed;
        await SaveSongAsync(CurrentFile.Song);
    }

    // Called by Fody when Transpose property changes
    private void OnTransposeChanged()
    {
        if (CurrentFile is null) return;
        CurrentFile.Song.Transpose = Transpose?.Key;
        _ = SaveSongAsync(CurrentFile.Song);

        // Transpose change requires playback rebuild
        SettingsRebuildRequired?.Invoke();
    }

    private async Task SaveSongAsync(Song song)
    {
        try
        {
            await using var db = _ioc.Get<LyreContext>();
            db.Songs.Update(song);
            await db.SaveChangesAsync();
        }
        catch { /* Ignore save errors */ }
    }

    #endregion
}
