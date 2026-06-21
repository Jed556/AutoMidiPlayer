using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.WPF.Controls.Snackbar;
using AutoMidiPlayer.WPF.Services.MidiShow;
using JetBrains.Annotations;
using Melanchall.DryWetMidi.Core;
using Stylet;
using StyletIoC;
using MidiFile = AutoMidiPlayer.Data.Midi.MidiFile;

namespace AutoMidiPlayer.WPF.ViewModels;

/// <summary>
/// Browse, search and download MIDI files from MidiShow (https://www.midishow.com).
/// Downloads use the signed-in user's own MidiShow account; credentials are stored
/// encrypted per-user via <see cref="MidiShowCredentialStore"/>.
/// </summary>
[UsedImplicitly]
public sealed class OnlineMidiViewModel : Screen
{
    private readonly IContainer _ioc;
    private readonly MainWindowViewModel _main;
    private readonly MidiShowClient _client = new();

    private bool _initialized;
    private CancellationTokenSource? _loadCts;

    /// <summary>True once a page load comes back empty (we've paged past the last page).</summary>
    private bool _reachedEnd;

    public OnlineMidiViewModel(IContainer ioc, MainWindowViewModel main)
    {
        _ioc = ioc;
        _main = main;
        _preview.Finished += OnPreviewFinished;

        // Preview and the main player both render through the same Windows synth, so they
        // must not play at once. When the main player starts, stop the preview cleanly
        // (releasing its synth device) — otherwise the preview dies mid-play and its device
        // is left in a bad state, breaking the next preview.
        _main.PlaybackControls.PlaybackStateChanged += OnMainPlaybackStateChanged;
    }

    private void OnMainPlaybackStateChanged(object? sender, EventArgs e)
    {
        if (!IsPreviewActive || !_main.PlaybackControls.IsPlaying)
            return;

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            StopPreview();
        else
            dispatcher.Invoke(StopPreview);
    }

    // Stop any preview when navigating away from Discover (does not touch the main player).
    protected override void OnClose() => StopPreview();

    #region Bindable state

    public BindableCollection<MidiShowItem> Results { get; } = new();

    public bool IsSignedIn { get; private set; }
    public string SignedInUser { get; private set; } = string.Empty;

    /// <summary>Username typed into the sign-in form.</summary>
    public string LoginUsername { get; set; } = string.Empty;

    /// <summary>Password typed into the sign-in form (bound from the PasswordBox).</summary>
    public string LoginPassword { get; set; } = string.Empty;

    /// <summary>Whether to persist credentials (encrypted) for next launch.</summary>
    public bool RememberMe { get; set; } = true;

    /// <summary>When true the password is shown as plain text (eye toggle).</summary>
    public bool IsPasswordRevealed { get; private set; }

    public void ToggleReveal()
    {
        IsPasswordRevealed = !IsPasswordRevealed;
        NotifyOfPropertyChange(nameof(IsPasswordRevealed));
    }

    private bool _isAccountFlyoutOpen;
    private DateTime _lastFlyoutCloseTime = DateTime.MinValue;

    /// <summary>Whether the account / sign-in popup is open.</summary>
    public bool IsAccountFlyoutOpen
    {
        get => _isAccountFlyoutOpen;
        set
        {
            if (_isAccountFlyoutOpen && !value)
                _lastFlyoutCloseTime = DateTime.UtcNow;
            SetAndNotify(ref _isAccountFlyoutOpen, value);
        }
    }

    public void ToggleAccountFlyout()
    {
        // When the popup closes via an outside click, the button click that follows would
        // immediately reopen it. Ignore a toggle that lands right after a close.
        if (!IsAccountFlyoutOpen && (DateTime.UtcNow - _lastFlyoutCloseTime).TotalMilliseconds < 250)
            return;

        IsAccountFlyoutOpen = !IsAccountFlyoutOpen;
    }

    public string SearchQuery { get; set; } = string.Empty;

    public int CurrentPage { get; private set; } = 1;

    public bool IsBusy { get; private set; }
    public bool IsDownloading { get; private set; }
    public string StatusMessage { get; private set; } = string.Empty;

    public bool IsNotBusy => !IsBusy;
    public bool CanGoToPreviousPage => CurrentPage > 1 && !IsBusy;
    public bool CanGoToNextPage => !IsBusy && !_reachedEnd;
    public bool HasResults => Results.Count > 0;
    public bool ShowEmptyState => !IsBusy && Results.Count == 0;

    #endregion

    protected override void OnActivate()
    {
        if (_initialized)
            return;

        _initialized = true;
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        // Show the (public) listing first so MIDIs appear after a single round-trip, then sign
        // in afterwards. Browsing/searching needs no auth — only download/preview do — so there
        // is no reason to make the user wait for the login round-trips before seeing content.
        // (LoginAsync starts from a fresh session anyway, so the anonymous browse cookies don't
        // interfere.) The "signed in" badge updates a moment after the list renders.
        await LoadAsync();
        await TrySignInFromStoreAsync();
    }

    #region Authentication

    private async Task TrySignInFromStoreAsync()
    {
        var credentials = MidiShowCredentialStore.Load();
        if (credentials is null)
            return;

        LoginUsername = credentials.Username;
        try
        {
            var success = await _client.LoginAsync(credentials.Username, credentials.Password);
            SetSignedIn(success, credentials.Username);
        }
        catch (Exception ex)
        {
            Logger.LogException(ex);
            SetSignedIn(false, credentials.Username);
        }
    }

    /// <summary>
    /// Signs in using the bound <see cref="LoginUsername"/> / <see cref="LoginPassword"/>.
    /// </summary>
    public async Task SignIn()
    {
        var username = (LoginUsername ?? string.Empty).Trim();
        var password = LoginPassword ?? string.Empty;

        Logger.LogStep("MIDISHOW_LOGIN", $"userLen={username.Length} | passLen={password.Length}");

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            SnackbarService.Warning("Sign in failed", "Enter your MidiShow username and password.");
            return;
        }

        SetBusy(true);
        StatusMessage = "Signing in to MidiShow...";
        try
        {
            var success = await _client.LoginAsync(username, password);
            SetSignedIn(success, username);

            if (success)
            {
                if (RememberMe)
                    MidiShowCredentialStore.Save(new MidiShowCredentials(username, password));

                LoginPassword = string.Empty;
                NotifyOfPropertyChange(nameof(LoginPassword));
                IsAccountFlyoutOpen = false;

                SnackbarService.Success("Signed in", $"Connected to MidiShow as {username}.");
            }
            else
            {
                SnackbarService.Danger("Sign in failed", "MidiShow rejected those credentials. Double-check your email and password.");
            }
        }
        catch (MidiShowException ex)
        {
            SnackbarService.Danger("Sign in failed", ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogException(ex);
            SnackbarService.Danger("Sign in failed", "An unexpected error occurred. Check your connection.");
        }
        finally
        {
            SetBusy(false);
            StatusMessage = string.Empty;
        }
    }

    public void SignOut()
    {
        _client.SignOut();
        MidiShowCredentialStore.Clear();
        SetSignedIn(false, string.Empty);
        IsAccountFlyoutOpen = false;
        SnackbarService.Info("Signed out", "Your MidiShow credentials were removed from this device.");
    }

    private void SetSignedIn(bool signedIn, string username)
    {
        IsSignedIn = signedIn;
        SignedInUser = signedIn ? username : string.Empty;
        NotifyOfPropertyChange(nameof(IsSignedIn));
        NotifyOfPropertyChange(nameof(SignedInUser));
    }

    #endregion

    #region Browse / Search

    /// <summary>Current MidiShow category slug ("" = all categories).</summary>
    public string SelectedCategorySlug { get; private set; } = "";

    /// <summary>Display name for the active category button.</summary>
    public string SelectedCategoryName { get; private set; } = "All categories";

    public async Task Search()
    {
        // A keyword search spans all categories, so reset the category filter.
        if (!string.IsNullOrEmpty(SelectedCategorySlug))
        {
            SelectedCategorySlug = "";
            SelectedCategoryName = "All categories";
            NotifyOfPropertyChange(nameof(SelectedCategoryName));
        }

        CurrentPage = 1;
        await LoadAsync();
    }

    /// <summary>Browses a MidiShow category (clears any keyword search).</summary>
    public async Task SetCategory(string slug, string name)
    {
        SelectedCategorySlug = slug ?? "";
        SelectedCategoryName = string.IsNullOrEmpty(name) ? "All categories" : name;

        // Category browse and keyword search are mutually exclusive on MidiShow.
        SearchQuery = "";
        NotifyOfPropertyChange(nameof(SearchQuery));
        NotifyOfPropertyChange(nameof(SelectedCategoryName));

        CurrentPage = 1;
        await LoadAsync();
    }

    public async Task Reload() => await LoadAsync();

    /// <summary>MidiShow sort key: "" = newest, "time_asc" = oldest, "popularity", "marks".</summary>
    public string SortKey { get; private set; } = "";

    public string SortLabel => SortKey switch
    {
        "time_asc" => "Oldest",
        "popularity" => "Most popular",
        "marks" => "Highest rated",
        _ => "Newest"
    };

    public async Task SetSort(string? key)
    {
        var newKey = key ?? "";
        if (newKey == SortKey)
            return;

        SortKey = newKey;
        NotifyOfPropertyChange(nameof(SortKey));
        NotifyOfPropertyChange(nameof(SortLabel));
        CurrentPage = 1;
        await LoadAsync();
    }

    /// <summary>Opens the MidiShow account registration page in the default browser.</summary>
    public void OpenRegisterPage()
    {
        const string url = "https://www.midishow.com/en/user/account/signup";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.LogException(ex);
            SnackbarService.Warning("Couldn't open the browser", $"Please visit {url} to create an account.");
        }
    }

    public async Task NextPage()
    {
        // Debounce: ignore rapid repeat clicks while a page is still loading, so we don't
        // queue a burst of requests or skip pages. Also stop once we've hit the last page.
        if (IsBusy || _reachedEnd)
            return;

        CurrentPage++;
        await LoadAsync();
    }

    public async Task PreviousPage()
    {
        if (IsBusy || CurrentPage <= 1)
            return;

        CurrentPage--;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCts = cts;

        var isSearch = !string.IsNullOrWhiteSpace(SearchQuery);
        SetBusy(true);
        StatusMessage = isSearch
            ? $"Searching \"{SearchQuery.Trim()}\"..."
            : (string.IsNullOrEmpty(SelectedCategorySlug) ? "Loading MIDI files..." : $"Loading {SelectedCategoryName}...");

        try
        {
            var items = isSearch
                ? await _client.SearchAsync(SearchQuery, CurrentPage, SortKey, cts.Token)
                : await _client.BrowseAsync(CurrentPage, SortKey, SelectedCategorySlug, cts.Token);

            if (cts.IsCancellationRequested)
                return;

            Results.Clear();
            Results.AddRange(items);

            // An empty page means we've run past the last page — stop "Next" from walking
            // into endless empty pages. Any page-1 navigation (search/category/sort) resets this.
            _reachedEnd = items.Count == 0 && CurrentPage > 1;

            var scope = isSearch
                ? $" for \"{SearchQuery.Trim()}\""
                : (string.IsNullOrEmpty(SelectedCategorySlug) ? "" : $" in {SelectedCategoryName}");

            StatusMessage = items.Count == 0
                ? $"No MIDI files found{scope}."
                : $"Showing {items.Count} result{(items.Count == 1 ? "" : "s")}{scope} (page {CurrentPage}).";
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer request.
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to load MidiShow listing.");
            Logger.LogException(ex);
            StatusMessage = "Could not reach MidiShow. Check your connection and try again.";
            SnackbarService.Danger("MidiShow", "Could not load the MIDI list.");
        }
        finally
        {
            // Only the current (newest) load owns the busy state. A superseded load reaching its
            // finally must NOT clear IsBusy — the newer request is still in flight.
            if (_loadCts == cts)
            {
                _loadCts = null;
                SetBusy(false);
            }
        }
    }

    private void SetBusy(bool busy)
    {
        IsBusy = busy;
        NotifyOfPropertyChange(nameof(IsBusy));
        NotifyOfPropertyChange(nameof(IsNotBusy));
        NotifyOfPropertyChange(nameof(CurrentPage));
        NotifyOfPropertyChange(nameof(CanGoToPreviousPage));
        NotifyOfPropertyChange(nameof(CanGoToNextPage));
        NotifyOfPropertyChange(nameof(HasResults));
        NotifyOfPropertyChange(nameof(ShowEmptyState));
    }

    #endregion

    #region Details

    private MidiShowItem? _detailItem;

    public MidiShowDetails? SelectedDetails { get; private set; }
    public bool IsDetailOpen { get; private set; }
    public bool IsDetailLoading { get; private set; }
    public bool HasDetails => SelectedDetails is not null;

    public async Task ShowDetailsAsync(MidiShowItem item)
    {
        if (item is null)
            return;

        _detailItem = item;
        SelectedDetails = null;
        IsDetailOpen = true;
        IsDetailLoading = true;
        NotifyOfPropertyChange(nameof(SelectedDetails));
        NotifyOfPropertyChange(nameof(HasDetails));
        NotifyOfPropertyChange(nameof(IsDetailOpen));
        NotifyOfPropertyChange(nameof(IsDetailLoading));

        try
        {
            var details = await _client.GetDetailsAsync(item);

            // If the user opened another track (or closed the panel) while this was loading,
            // a stale response must not overwrite the newer one's details.
            if (!ReferenceEquals(_detailItem, item))
                return;

            SelectedDetails = details;
        }
        catch (Exception ex)
        {
            if (!ReferenceEquals(_detailItem, item))
                return; // superseded — let the newer request own the UI

            Logger.LogException(ex);
            SnackbarService.Danger("Couldn't load details", "Could not load this MIDI's details.");
            CloseDetails();
            return;
        }
        finally
        {
            // Only the most recent request clears the loading state / notifies.
            if (ReferenceEquals(_detailItem, item))
            {
                IsDetailLoading = false;
                NotifyOfPropertyChange(nameof(SelectedDetails));
                NotifyOfPropertyChange(nameof(HasDetails));
                NotifyOfPropertyChange(nameof(IsDetailLoading));
            }
        }
    }

    public void CloseDetails()
    {
        IsDetailOpen = false;
        SelectedDetails = null;
        _detailItem = null;
        NotifyOfPropertyChange(nameof(IsDetailOpen));
        NotifyOfPropertyChange(nameof(SelectedDetails));
        NotifyOfPropertyChange(nameof(HasDetails));
    }

    public async Task AddSelectedToSongs()
    {
        if (_detailItem is not null)
            await AddToSongsAsync(_detailItem);
    }

    public void OpenDetailPage()
    {
        var url = SelectedDetails?.PageUrl ?? _detailItem?.PageUrl;
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.LogException(ex);
        }
    }

    #endregion

    #region Download

    /// <summary>
    /// Fetches the selected MIDI from MidiShow and adds it straight into the Songs library.
    /// </summary>
    public async Task AddToSongsAsync(MidiShowItem item)
    {
        if (item is null)
            return;

        if (!IsSignedIn)
        {
            SnackbarService.Warning("Sign in required", "Sign in with your MidiShow account first.");
            return;
        }

        // Preview and download share the same network/decode pipeline; only one at a time.
        if (IsDownloading)
        {
            SnackbarService.Info("Please wait", "Another track is still loading. Try again in a moment.");
            return;
        }

        IsDownloading = true;
        NotifyOfPropertyChange(nameof(IsDownloading));
        StatusMessage = $"Adding \"{item.Title}\" to Songs...";

        try
        {
            var result = await _client.DownloadAsync(item.PageUrl);
            var path = await SaveMidiAsync(result, item);

            await _main.FileService.AddFiles(new[] { path });

            SnackbarService.Success("Added to Songs", $"\"{result.Title}\" is now in your Songs library.");
        }
        catch (MidiShowException ex)
        {
            var secondary = ex.Reason switch
            {
                MidiShowDownloadError.NotAuthenticated => "Your MidiShow session expired. Sign in again.",
                MidiShowDownloadError.NotFound => "This track is no longer available.",
                _ => ex.Message
            };

            if (ex.Reason == MidiShowDownloadError.NotAuthenticated)
                SetSignedIn(false, SignedInUser);

            SnackbarService.Danger("Couldn't add to Songs", secondary);
        }
        catch (Exception ex)
        {
            Logger.Log("MidiShow add-to-songs failed.");
            Logger.LogException(ex);
            SnackbarService.Danger("Couldn't add to Songs", "An unexpected error occurred.");
        }
        finally
        {
            IsDownloading = false;
            NotifyOfPropertyChange(nameof(IsDownloading));
            StatusMessage = string.Empty;
        }
    }

    private readonly MidiShowPreviewPlayer _preview = new();
    private System.Windows.Threading.DispatcherTimer? _previewTimer;
    private bool _previewScrubbing;

    /// <summary>True while the preview mini-player popup is open.</summary>
    public bool IsPreviewActive { get; private set; }

    /// <summary>True when the preview is playing (vs paused) — drives the play/pause icon.</summary>
    public bool IsPreviewPlaying { get; private set; }
    public string PreviewPlayPauseIcon => IsPreviewPlaying ? "PauseCircle24" : "PlayCircle24";

    public string PreviewTitle { get; private set; } = string.Empty;

    public double PreviewDurationSeconds { get; private set; }
    public double PreviewPositionSeconds { get; set; }
    public string PreviewPositionText { get; private set; } = "0:00";
    public string PreviewDurationText { get; private set; } = "0:00";

    /// <summary>
    /// Downloads (only when clicked) and plays the MIDI on a SEPARATE preview player —
    /// it does not touch the main player, the queue, the opened file or Listen Mode, and
    /// nothing is added to the Songs library. A standalone "listen before you download".
    /// </summary>
    public async Task PreviewAsync(MidiShowItem item)
    {
        if (item is null)
            return;

        if (!IsSignedIn)
        {
            SnackbarService.Warning("Sign in required", "Sign in with your MidiShow account to preview.");
            return;
        }

        // Preview and download share the same network/decode pipeline; only one at a time.
        if (IsDownloading)
        {
            SnackbarService.Info("Please wait", "Another track is still loading. Try again in a moment.");
            return;
        }

        IsDownloading = true;
        NotifyOfPropertyChange(nameof(IsDownloading));
        StatusMessage = $"Loading preview of \"{item.Title}\"...";

        // Whether we paused the main player to make room for this preview. If the preview
        // then fails to start, we resume the main player so a failed preview doesn't leave
        // the user's music silently paused.
        var pausedMain = false;

        try
        {
            var result = await _client.DownloadAsync(item.PageUrl);

            // The synth allows only one open handle, so reuse the main player's device.
            var synth = _main.PlaybackEngine.PreviewSynthDevice;
            if (synth is null)
            {
                SnackbarService.Danger("Preview unavailable", "The audio synth (Microsoft GS Wavetable Synth) isn't available.");
                return;
            }

            // Pause the main player so the preview doesn't overlap with it on the shared synth.
            if (_main.PlaybackControls.IsPlaying)
            {
                await _main.PlaybackControls.PlayPause();
                pausedMain = true;
            }

            // Play on the shared synth (off the UI thread; reading the MIDI).
            await Task.Run(() => _preview.Play(result.Data, synth));

            PreviewTitle = result.Title;
            IsPreviewActive = true;
            IsPreviewPlaying = true;
            _previewScrubbing = false;
            PreviewDurationSeconds = Math.Max(0.1, _preview.Duration.TotalSeconds);
            PreviewPositionSeconds = 0;
            PreviewDurationText = FormatTime(_preview.Duration);
            PreviewPositionText = "0:00";

            NotifyOfPropertyChange(nameof(PreviewTitle));
            NotifyOfPropertyChange(nameof(IsPreviewActive));
            NotifyOfPropertyChange(nameof(IsPreviewPlaying));
            NotifyOfPropertyChange(nameof(PreviewPlayPauseIcon));
            NotifyOfPropertyChange(nameof(PreviewDurationSeconds));
            NotifyOfPropertyChange(nameof(PreviewPositionSeconds));
            NotifyOfPropertyChange(nameof(PreviewDurationText));
            NotifyOfPropertyChange(nameof(PreviewPositionText));

            StartPreviewTimer();
        }
        catch (MidiShowException ex)
        {
            await ResumeMainIfPreviewFailed(pausedMain);

            if (ex.Reason == MidiShowDownloadError.NotAuthenticated)
                SetSignedIn(false, SignedInUser);

            SnackbarService.Danger("Preview failed", ex.Reason == MidiShowDownloadError.NotAuthenticated
                ? "Your MidiShow session expired. Sign in again."
                : ex.Message);
        }
        catch (Exception ex)
        {
            await ResumeMainIfPreviewFailed(pausedMain);

            Logger.Log("MidiShow preview failed.");
            Logger.LogException(ex);
            SnackbarService.Danger("Preview failed", "Could not play this MIDI. The audio synth may be unavailable.");
        }
        finally
        {
            IsDownloading = false;
            NotifyOfPropertyChange(nameof(IsDownloading));
            StatusMessage = string.Empty;
        }
    }

    /// <summary>
    /// If we paused the main player to make room for a preview that never actually started,
    /// resume it — otherwise a failed preview would leave the user's music silently paused.
    /// </summary>
    private async Task ResumeMainIfPreviewFailed(bool pausedMain)
    {
        if (pausedMain && !IsPreviewActive && !_main.PlaybackControls.IsPlaying)
            await _main.PlaybackControls.PlayPause();
    }

    public async Task PreviewSelectedAsync()
    {
        if (_detailItem is not null)
            await PreviewAsync(_detailItem);
    }

    /// <summary>Toggle play/pause on the preview.</summary>
    public void PreviewPlayPause()
    {
        if (!IsPreviewActive)
            return;

        _preview.TogglePlayPause();
        IsPreviewPlaying = _preview.IsPlaying;
        NotifyOfPropertyChange(nameof(IsPreviewPlaying));
        NotifyOfPropertyChange(nameof(PreviewPlayPauseIcon));
    }

    public void StopPreview()
    {
        StopPreviewTimer();
        _preview.Stop();
        IsPreviewActive = false;
        IsPreviewPlaying = false;
        PreviewPositionSeconds = 0;
        NotifyOfPropertyChange(nameof(IsPreviewActive));
        NotifyOfPropertyChange(nameof(IsPreviewPlaying));
        NotifyOfPropertyChange(nameof(PreviewPlayPauseIcon));
        NotifyOfPropertyChange(nameof(PreviewPositionSeconds));
    }

    // Called from the view while the user drags the seek slider.
    public void BeginPreviewScrub() => _previewScrubbing = true;

    public void EndPreviewScrub()
    {
        _preview.Seek(TimeSpan.FromSeconds(PreviewPositionSeconds));
        _previewScrubbing = false;
        PreviewPositionText = FormatTime(TimeSpan.FromSeconds(PreviewPositionSeconds));
        NotifyOfPropertyChange(nameof(PreviewPositionText));
    }

    private void StartPreviewTimer()
    {
        if (_previewTimer is null)
        {
            _previewTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _previewTimer.Tick += (_, _) => OnPreviewTick();
        }
        _previewTimer.Start();
    }

    private void StopPreviewTimer() => _previewTimer?.Stop();

    private void OnPreviewTick()
    {
        if (!IsPreviewActive)
            return;

        var playing = _preview.IsPlaying;
        if (playing != IsPreviewPlaying)
        {
            IsPreviewPlaying = playing;
            NotifyOfPropertyChange(nameof(IsPreviewPlaying));
            NotifyOfPropertyChange(nameof(PreviewPlayPauseIcon));
        }

        if (_previewScrubbing)
            return;

        var pos = _preview.CurrentTime;
        PreviewPositionSeconds = pos.TotalSeconds;
        PreviewPositionText = FormatTime(pos);
        NotifyOfPropertyChange(nameof(PreviewPositionSeconds));
        NotifyOfPropertyChange(nameof(PreviewPositionText));
    }

    private void OnPreviewFinished()
    {
        void Apply() => StopPreview();

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            Apply();
        else
            dispatcher.Invoke(Apply);
    }

    private static string FormatTime(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }

    private static async Task<string> SaveMidiAsync(MidiShowDownloadResult result, MidiShowItem item)
    {
        var directory = AppPaths.EnsureOnlineMidiDirectory();
        var baseName = SanitizeFileName(string.IsNullOrWhiteSpace(result.Title) ? item.Title : result.Title);
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = $"midishow-{item.Id}";

        var path = Path.Combine(directory, baseName + ".mid");

        // Avoid clobbering an existing download with the same title.
        var counter = 1;
        while (File.Exists(path))
        {
            path = Path.Combine(directory, $"{baseName} ({counter++}).mid");
        }

        await NormalizeToFileAsync(result.Data, path);
        return path;
    }

    /// <summary>
    /// MidiShow's MIDI files are slightly non-strict (the final chunk length trips the
    /// default reader's NotEnoughBytesException). Read leniently and re-write a clean,
    /// standard MIDI so the rest of the app accepts it without the "Bad MIDI" dialog.
    /// </summary>
    private static Task NormalizeToFileAsync(byte[] data, string path) => Task.Run(() =>
    {
        try
        {
            var lenient = new ReadingSettings
            {
                NotEnoughBytesPolicy = NotEnoughBytesPolicy.Ignore,
                InvalidChunkSizePolicy = InvalidChunkSizePolicy.Ignore,
                UnexpectedTrackChunksCountPolicy = UnexpectedTrackChunksCountPolicy.Ignore,
                ExtraTrackChunkPolicy = ExtraTrackChunkPolicy.Read
            };

            using var stream = new MemoryStream(data);
            var midi = Melanchall.DryWetMidi.Core.MidiFile.Read(stream, lenient);
            midi.Write(path, overwriteFile: true);
        }
        catch
        {
            File.WriteAllBytes(path, data);
        }
    });

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        cleaned = Regex.Replace(cleaned, "\\s+", " ").Trim().Trim('.');

        // Keep file names to a reasonable length.
        return cleaned.Length > 120 ? cleaned[..120].Trim() : cleaned;
    }

    #endregion

    // NOTE: Do NOT dispose the MidiShowClient here. Stylet's conductor closes this screen
    // every time the user navigates to another page, but the same ViewModel instance is
    // reused for the app's lifetime. Disposing the HttpClient on navigation would drop the
    // authenticated session, so returning and downloading would fail with
    // "Could not load the MIDI page". The client lives with the app and is released on exit.
}
