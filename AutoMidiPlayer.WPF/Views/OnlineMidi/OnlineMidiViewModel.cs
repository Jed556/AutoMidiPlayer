using System;
using System.Collections.Generic;
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
/// Downloads run through a <see cref="MidiShowAccountPool"/> that rotates across the user's
/// configured accounts (password- or cookie-based) and fails over when one is limited;
/// accounts are stored encrypted per-user via <see cref="MidiShowAccountStore"/>.
/// </summary>
[UsedImplicitly]
public sealed class OnlineMidiViewModel : Screen
{
    private readonly IContainer _ioc;
    private readonly MainWindowViewModel _main;
    private readonly MidiShowAccountPool _pool = new();

    private bool _initialized;
    private CancellationTokenSource? _loadCts;

    /// <summary>True once a page load comes back empty (we've paged past the last page).</summary>
    private bool _reachedEnd;

    public OnlineMidiViewModel(IContainer ioc, MainWindowViewModel main)
    {
        _ioc = ioc;
        _main = main;
        _preview.Finished += OnPreviewFinished;
        _pool.Changed += OnPoolChanged;

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

    /// <summary>The configured MidiShow accounts, with live health status, for the account flyout.</summary>
    public BindableCollection<MidiShowAccountRow> Accounts { get; } = new();

    /// <summary>True when at least one MidiShow account is configured (drives the account badge).</summary>
    public bool HasAnyAccount { get; private set; }

    /// <summary>Label for the account toolbar button when accounts exist, e.g. "Accounts (2)".</summary>
    public string AccountButtonText => $"Accounts ({Accounts.Count})";

    /// <summary>One-line pool summary, e.g. "2 accounts · 1 active".</summary>
    public string AccountSummary
    {
        get
        {
            var total = Accounts.Count;
            if (total == 0)
                return "No accounts yet";
            var active = Accounts.Count(a => a.State == MidiShowAccountState.Active);
            return $"{total} account{(total == 1 ? "" : "s")} · {active} active";
        }
    }

    // ----- Add-account form -----

    /// <summary>Username/email typed into the add-account form.</summary>
    public string NewUsername { get; set; } = string.Empty;

    /// <summary>Password typed into the add-account form (bound from the PasswordBox).</summary>
    public string NewPassword { get; set; } = string.Empty;

    /// <summary>Display label for a cookie-based account.</summary>
    public string NewCookieLabel { get; set; } = string.Empty;

    /// <summary>Raw cookie header pasted for a cookie-based account.</summary>
    public string NewCookies { get; set; } = string.Empty;

    /// <summary>When true the add form collects cookies instead of a password.</summary>
    private bool _isAddingCookie;
    public bool IsAddingCookie
    {
        get => _isAddingCookie;
        set
        {
            SetAndNotify(ref _isAddingCookie, value);
            NotifyOfPropertyChange(nameof(IsAddingPassword));
            SetAddAccountError(string.Empty);
        }
    }
    public bool IsAddingPassword => !IsAddingCookie;

    /// <summary>
    /// Inline validation / error message shown inside the account flyout. The flyout popup
    /// covers the snackbar area, so add-account failures are surfaced here instead.
    /// </summary>
    public string AddAccountError { get; private set; } = string.Empty;
    public bool HasAddAccountError => !string.IsNullOrEmpty(AddAccountError);

    private void SetAddAccountError(string message)
    {
        AddAccountError = message;
        NotifyOfPropertyChange(nameof(AddAccountError));
        NotifyOfPropertyChange(nameof(HasAddAccountError));
    }

    /// <summary>
    /// True while an add-account sign-in is in flight. Drives a spinner ON the flyout's Add
    /// button — NOT the results busy overlay (the sign-in happens inside the popup, so the
    /// loading belongs there, not over the list behind it).
    /// </summary>
    public bool IsAddingAccount { get; private set; }
    public bool IsNotAddingAccount => !IsAddingAccount;

    private void SetAddingAccount(bool adding)
    {
        IsAddingAccount = adding;
        NotifyOfPropertyChange(nameof(IsAddingAccount));
        NotifyOfPropertyChange(nameof(IsNotAddingAccount));
    }

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
        // the saved accounts in afterwards. Browsing/searching needs no auth — only
        // download/preview do — so there is no reason to make the user wait for login
        // round-trips before seeing content. The account badge updates as each one connects.
        await LoadAsync();
        RefreshAccounts();
        await _pool.RestoreAsync();
    }

    #region Accounts

    /// <summary>Rebuilds the bindable account list from the pool's current snapshot (UI thread).</summary>
    private void RefreshAccounts()
    {
        var snapshot = _pool.Snapshot();
        Accounts.Clear();
        Accounts.AddRange(snapshot.Select(s => new MidiShowAccountRow(s)));

        HasAnyAccount = snapshot.Count > 0;
        NotifyOfPropertyChange(nameof(HasAnyAccount));
        NotifyOfPropertyChange(nameof(AccountButtonText));
        NotifyOfPropertyChange(nameof(AccountSummary));
    }

    private void OnPoolChanged()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            RefreshAccounts();
        else
            dispatcher.Invoke(RefreshAccounts);
    }

    /// <summary>Adds a password-based MidiShow account from the add form.</summary>
    public async Task AddPasswordAccount()
    {
        var username = (NewUsername ?? string.Empty).Trim();
        var password = NewPassword ?? string.Empty;

        Logger.LogStep("MIDISHOW_ADD_ACCOUNT", $"mode=password userLen={username.Length} passLen={password.Length}");

        SetAddAccountError(string.Empty);
        SetAddingAccount(true);
        try
        {
            var (ok, message) = await _pool.AddPasswordAsync(username, password);
            if (ok)
            {
                NewUsername = string.Empty;
                NewPassword = string.Empty;
                NotifyOfPropertyChange(nameof(NewUsername));
                NotifyOfPropertyChange(nameof(NewPassword));
                SnackbarService.Success("Account added", message);
            }
            else
            {
                SetAddAccountError(message);
            }
        }
        catch (Exception ex)
        {
            Logger.LogException(ex);
            SetAddAccountError("An unexpected error occurred. Check your connection.");
        }
        finally
        {
            SetAddingAccount(false);
        }
    }

    /// <summary>Adds a cookie-based MidiShow account from the add form.</summary>
    public async Task AddCookieAccount()
    {
        var label = (NewCookieLabel ?? string.Empty).Trim();
        var cookies = NewCookies ?? string.Empty;

        Logger.LogStep("MIDISHOW_ADD_ACCOUNT", $"mode=cookie labelLen={label.Length} cookieLen={cookies.Length}");

        SetAddAccountError(string.Empty);
        SetAddingAccount(true);
        try
        {
            var (ok, message) = await _pool.AddCookieAsync(label, cookies);
            if (ok)
            {
                NewCookieLabel = string.Empty;
                NewCookies = string.Empty;
                NotifyOfPropertyChange(nameof(NewCookieLabel));
                NotifyOfPropertyChange(nameof(NewCookies));
                SnackbarService.Success("Account added", message);
            }
            else
            {
                SetAddAccountError(message);
            }
        }
        catch (Exception ex)
        {
            Logger.LogException(ex);
            SetAddAccountError("An unexpected error occurred. Check your connection.");
        }
        finally
        {
            SetAddingAccount(false);
        }
    }

    /// <summary>Removes an account from the pool (and from disk).</summary>
    public void RemoveAccount(MidiShowAccountRow row)
    {
        if (row is null)
            return;

        _pool.Remove(row.Username);
        SnackbarService.Info("Account removed", $"\"{row.Username}\" was removed from this device.");
    }

    /// <summary>Copies an account's live session cookies to the clipboard.</summary>
    public void CopyCookies(MidiShowAccountRow row)
    {
        if (row is null)
            return;

        var cookies = _pool.ExportCookies(row.Username);
        if (string.IsNullOrEmpty(cookies))
        {
            SnackbarService.Warning("No cookies", "This account isn't signed in yet, so there are no cookies to copy.");
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(cookies);
            SnackbarService.Success("Cookies copied", $"Session cookies for \"{row.Username}\" copied to the clipboard.");
        }
        catch (Exception ex)
        {
            Logger.LogException(ex);
            SnackbarService.Danger("Copy failed", "Could not access the clipboard.");
        }
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

        Results.Clear();
        for (int i = 0; i < 8; i++)
        {
            Results.Add(new MidiShowItem 
            { 
                Id = $"skeleton_{i}", 
                IsLoading = true,
                Description = "...",
                Category = "...",
                Tags = "..."
            });
        }

        try
        {
            var items = isSearch
                ? await _pool.SearchAsync(SearchQuery, CurrentPage, SortKey, cts.Token)
                : await _pool.BrowseAsync(CurrentPage, SortKey, SelectedCategorySlug, cts.Token);

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
                : $"Showing {items.Count} result{(items.Count == 1 ? "" : "s")}{scope}.";

            // Fetching extended details sequentially is no longer necessary since all data 
            // is available in the summary listing HTML.
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
            if (_loadCts == cts)
                Results.Clear();
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
            var details = await _pool.GetDetailsAsync(item);

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

        if (!_pool.HasAccounts)
        {
            SnackbarService.Warning("Account required", "Add a MidiShow account first.");
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
            // The pool rotates across accounts and fails over internally, so by the time an
            // exception reaches here every usable account has already been tried.
            var result = await _pool.DownloadAsync(item.PageUrl);
            var path = await SaveMidiAsync(result, item);

            await _main.FileService.AddFiles(new[] { path });

            SnackbarService.Success("Added to Songs", $"\"{result.Title}\" is now in your Songs library.");
        }
        catch (MidiShowException ex)
        {
            switch (ex.Reason)
            {
                case MidiShowDownloadError.Unavailable:
                    SnackbarService.Warning("MidiShow downloads paused", ex.Message);
                    break;
                case MidiShowDownloadError.LimitReached:
                    SnackbarService.Warning("Download limit reached", ex.Message);
                    break;
                case MidiShowDownloadError.RiskControlled:
                    SnackbarService.Warning("Account risk control", ex.Message);
                    break;
                default:
                    SnackbarService.Danger("Couldn't add to Songs", ex.Reason switch
                    {
                        MidiShowDownloadError.NotAuthenticated => "Could not sign in to any of your MidiShow accounts. Re-add one.",
                        MidiShowDownloadError.NotFound => "This track is no longer available.",
                        _ => ex.Message
                    });
                    break;
            }
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

        if (!_pool.HasAccounts)
        {
            SnackbarService.Warning("Account required", "Add a MidiShow account to preview.");
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
            var result = await _pool.DownloadAsync(item.PageUrl);

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

            switch (ex.Reason)
            {
                case MidiShowDownloadError.Unavailable:
                    SnackbarService.Warning("MidiShow previews paused", ex.Message);
                    break;
                case MidiShowDownloadError.LimitReached:
                    SnackbarService.Warning("Download limit reached", ex.Message);
                    break;
                case MidiShowDownloadError.RiskControlled:
                    SnackbarService.Warning("Account risk control", ex.Message);
                    break;
                default:
                    SnackbarService.Danger("Preview failed", ex.Reason == MidiShowDownloadError.NotAuthenticated
                        ? "Could not sign in to any of your MidiShow accounts. Re-add one."
                        : ex.Message);
                    break;
            }
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

        await NormalizeToFileAsync(result.Data, path, result.TrackNames);
        return path;
    }

    /// <summary>
    /// MidiShow's MIDI files are slightly non-strict (the final chunk length trips the
    /// default reader's NotEnoughBytesException). Read leniently and re-write a clean,
    /// standard MIDI so the rest of the app accepts it without the "Bad MIDI" dialog.
    /// </summary>
    private static Task NormalizeToFileAsync(byte[] data, string path, System.Collections.Generic.Dictionary<int, string>? trackNames = null) => Task.Run(() =>
    {
        try
        {
            var lenient = new ReadingSettings
            {
                NotEnoughBytesPolicy = NotEnoughBytesPolicy.Ignore,
                InvalidChunkSizePolicy = InvalidChunkSizePolicy.Ignore,
                UnexpectedTrackChunksCountPolicy = UnexpectedTrackChunksCountPolicy.Ignore,
                ExtraTrackChunkPolicy = ExtraTrackChunkPolicy.Read,
                TextEncoding = System.Text.Encoding.UTF8
            };

            var writeSettings = new WritingSettings
            {
                TextEncoding = System.Text.Encoding.UTF8
            };

            using var stream = new MemoryStream(data);
            var midi = Melanchall.DryWetMidi.Core.MidiFile.Read(stream, lenient);
            
            if (trackNames != null && trackNames.Count > 0)
            {
                var trackChunks = midi.GetTrackChunks().ToList();
                for (int i = 0; i < trackChunks.Count; i++)
                {
                    if (trackNames.TryGetValue(i, out var name))
                    {
                        var track = trackChunks[i];
                        var existingInstName = track.Events.OfType<Melanchall.DryWetMidi.Core.InstrumentNameEvent>().FirstOrDefault();
                        if (existingInstName != null)
                        {
                            existingInstName.Text = name;
                        }
                        else
                        {
                            track.Events.Insert(0, new Melanchall.DryWetMidi.Core.InstrumentNameEvent(name));
                        }
                    }
                }
            }

            midi.Write(path, overwriteFile: true, settings: writeSettings);
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

    // NOTE: Do NOT dispose the MidiShowAccountPool here. Stylet's conductor closes this screen
    // every time the user navigates to another page, but the same ViewModel instance is
    // reused for the app's lifetime. Disposing the pool on navigation would drop every
    // authenticated session, so returning and downloading would fail. The pool lives with the
    // app and is released on exit.
}

/// <summary>
/// A row in the account flyout: one MidiShow account plus its current health, with a friendly
/// status label, colour and icon for the list. Rebuilt from the pool snapshot on every change.
/// </summary>
public sealed class MidiShowAccountRow
{
    public MidiShowAccountRow(MidiShowAccountStatus status)
    {
        Username = status.Username;
        IsCookieBased = status.IsCookieBased;
        State = status.State;
    }

    public string Username { get; }
    public bool IsCookieBased { get; }
    public MidiShowAccountState State { get; }

    public string TypeLabel => IsCookieBased ? "Cookie" : "Password";

    public string StatusText => State switch
    {
        MidiShowAccountState.SigningIn => "Signing in…",
        MidiShowAccountState.Active => "Active",
        MidiShowAccountState.Limited => "Limit reached",
        MidiShowAccountState.RiskControlled => "Risk control",
        MidiShowAccountState.AuthFailed => "Sign-in failed",
        _ => "Idle"
    };

    /// <summary>A coloured dot for the row: green active, amber limited/risk, red failed, grey idle.</summary>
    public System.Windows.Media.Brush StatusBrush => State switch
    {
        MidiShowAccountState.Active => Frozen(0x2E, 0xA0, 0x43),         // green
        MidiShowAccountState.SigningIn => Frozen(0x3B, 0x82, 0xF6),      // blue
        MidiShowAccountState.Limited => Frozen(0xE5, 0xA8, 0x00),        // amber
        MidiShowAccountState.RiskControlled => Frozen(0xE5, 0xA8, 0x00), // amber
        MidiShowAccountState.AuthFailed => Frozen(0xE0, 0x4F, 0x4F),     // red
        _ => Frozen(0x9A, 0x9A, 0x9A)                                    // grey
    };

    public bool CanCopyCookies => State == MidiShowAccountState.Active;

    private static System.Windows.Media.Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
