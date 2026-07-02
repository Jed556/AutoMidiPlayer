using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMidiPlayer.Data;

namespace AutoMidiPlayer.WPF.Services.MidiShow;

/// <summary>Lifecycle/health of one account in the pool, for display.</summary>
public enum MidiShowAccountState
{
    /// <summary>Configured but not signed in yet.</summary>
    Idle,
    SigningIn,
    /// <summary>Signed in and usable for downloads.</summary>
    Active,
    /// <summary>Hit a download quota / points / VIP wall — cooling down.</summary>
    Limited,
    /// <summary>Flagged by MidiShow risk control — cooling down.</summary>
    RiskControlled,
    /// <summary>Sign-in failed (bad password / expired cookies).</summary>
    AuthFailed
}

/// <summary>A read-only snapshot of one account's identity and current health.</summary>
public sealed record MidiShowAccountStatus(string Username, bool IsCookieBased, MidiShowAccountState State);

/// <summary>
/// Manages a pool of MidiShow accounts and spreads downloads across them. Browsing, searching
/// and details run on a single anonymous session (no auth needed). Downloads pick a usable
/// account in round-robin order and, when one is blocked (quota / risk control / expired
/// session), automatically fail over to the next — so a single limited account no longer
/// breaks downloading. Mirrors the multi-account rotation in the reference self-host tool.
/// </summary>
public sealed class MidiShowAccountPool : IDisposable
{
    private sealed class Entry
    {
        public required MidiShowAccount Account;
        public MidiShowClient? Client;
        public bool LoggedIn;
        public DateTime CooldownUntilUtc = DateTime.MinValue;
        public MidiShowAccountState State = MidiShowAccountState.Idle;
    }

    private static readonly TimeSpan LimitCooldown = TimeSpan.FromHours(6);
    private static readonly TimeSpan RiskCooldown = TimeSpan.FromHours(2);

    private readonly object _lock = new();
    private readonly List<Entry> _entries = new();
    private readonly MidiShowClient _browseClient = new();
    private int _rotation;

    /// <summary>Raised (off the UI thread) whenever the set of accounts or their health changes.</summary>
    public event Action? Changed;

    #region Snapshot / counts

    public IReadOnlyList<MidiShowAccountStatus> Snapshot()
    {
        lock (_lock)
            return _entries.Select(e => new MidiShowAccountStatus(
                e.Account.Username, e.Account.IsCookieBased, EffectiveState(e))).ToList();
    }

    public int AccountCount
    {
        get { lock (_lock) return _entries.Count; }
    }

    /// <summary>Accounts that can be tried for a download right now (signed in or untried, no active cooldown).</summary>
    public int UsableCount
    {
        get
        {
            lock (_lock)
                return _entries.Count(e => EffectiveState(e) is MidiShowAccountState.Active or MidiShowAccountState.Idle or MidiShowAccountState.SigningIn);
        }
    }

    public bool HasAccounts => AccountCount > 0;

    // A cooled-down account presents as Idle again once its cooldown elapses (it becomes retryable).
    private static MidiShowAccountState EffectiveState(Entry e)
    {
        if (e.State is MidiShowAccountState.Limited or MidiShowAccountState.RiskControlled
            && DateTime.UtcNow >= e.CooldownUntilUtc)
            return MidiShowAccountState.Idle;
        return e.State;
    }

    private void RaiseChanged() => Changed?.Invoke();

    #endregion

    #region Load / add / remove

    /// <summary>
    /// Loads persisted accounts and signs each in (sequentially, in the background) so the pool
    /// is ready for downloads. Safe to call once at startup. Browsing works before this finishes.
    /// </summary>
    public async Task RestoreAsync(CancellationToken ct = default)
    {
        var accounts = MidiShowAccountStore.Load();
        if (accounts.Count == 0)
            return;

        lock (_lock)
        {
            _entries.Clear();
            foreach (var account in accounts)
                _entries.Add(new Entry { Account = account });
        }
        RaiseChanged();

        // Sign each account in one at a time — N fresh sessions at once would look botty.
        foreach (var entry in SnapshotEntries())
        {
            ct.ThrowIfCancellationRequested();
            try { await EnsureLoggedInAsync(entry, ct); }
            catch (Exception ex) { Logger.LogException(ex); }
            RaiseChanged();
        }
    }

    /// <summary>Adds a password account: verifies the login, then persists and keeps the session.</summary>
    public async Task<(bool ok, string message)> AddPasswordAsync(string username, string password, CancellationToken ct = default)
    {
        username = (username ?? string.Empty).Trim();
        password ??= string.Empty;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return (false, "Enter your MidiShow username and password.");
        if (ContainsAccount(username))
            return (false, $"\"{username}\" is already in your accounts.");

        var client = new MidiShowClient();
        try
        {
            var ok = await client.LoginAsync(username, password, ct);
            if (!ok)
            {
                client.Dispose();
                return (false, "MidiShow rejected those credentials. Double-check your email and password.");
            }
        }
        catch (MidiShowException ex)
        {
            client.Dispose();
            return (false, ex.Message);
        }

        AddEntry(new Entry
        {
            Account = MidiShowAccount.FromPassword(username, password),
            Client = client,
            LoggedIn = true,
            State = MidiShowAccountState.Active
        });
        return (true, $"Added MidiShow account {username}.");
    }

    /// <summary>Adds a cookie account: verifies the imported session, then persists it.</summary>
    public async Task<(bool ok, string message)> AddCookieAsync(string label, string cookies, CancellationToken ct = default)
    {
        label = (label ?? string.Empty).Trim();
        cookies = (cookies ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(cookies))
            return (false, "Paste the cookie header from a signed-in MidiShow browser tab.");
        if (string.IsNullOrWhiteSpace(label))
            label = "Cookie account";
        if (ContainsAccount(label))
            return (false, $"\"{label}\" is already in your accounts.");

        var client = new MidiShowClient();
        var ok = await client.LoginByCookies(cookies, ct);
        if (!ok)
        {
            client.Dispose();
            return (false, "Those cookies don't contain a valid MidiShow session (missing _identity). Copy them again while signed in.");
        }

        AddEntry(new Entry
        {
            Account = MidiShowAccount.FromCookies(label, cookies),
            Client = client,
            LoggedIn = true,
            State = MidiShowAccountState.Active
        });
        return (true, $"Added MidiShow account {label}.");
    }

    public void Remove(string username)
    {
        lock (_lock)
        {
            var entry = _entries.FirstOrDefault(e => string.Equals(e.Account.Username, username, StringComparison.Ordinal));
            if (entry is null)
                return;

            entry.Client?.Dispose();
            _entries.Remove(entry);
            Persist();
        }
        RaiseChanged();
    }

    public void Clear()
    {
        lock (_lock)
        {
            foreach (var entry in _entries)
                entry.Client?.Dispose();
            _entries.Clear();
            MidiShowAccountStore.Clear();
        }
        RaiseChanged();
    }

    /// <summary>Exports the live session cookies for an account (for re-use elsewhere).</summary>
    public string? ExportCookies(string username)
    {
        lock (_lock)
        {
            var entry = _entries.FirstOrDefault(e => string.Equals(e.Account.Username, username, StringComparison.Ordinal));
            return entry?.Client?.ExportCookies();
        }
    }

    #endregion

    #region Browse / search / details (anonymous session)

    public MidiShowPageResult? TryGetCachedBrowsePage(int page, string sort, string category)
        => MidiShowCache.TryLoadBrowsePage(page, sort, category);

    public MidiShowPageResult? TryGetCachedSearchPage(string query, int page, string sort)
        => MidiShowCache.TryLoadSearchPage(query, page, sort);

    public async Task<MidiShowPageResult> BrowseAsync(int page = 1, string sort = "", string category = "", bool forceRefresh = false, CancellationToken ct = default)
    {
        if (!forceRefresh)
        {
            var cached = MidiShowCache.TryLoadBrowsePage(page, sort, category);
            if (cached is not null)
                return cached;
        }

        var result = await _browseClient.BrowseAsync(page, sort, category, ct);
        _ = MidiShowCache.SaveBrowsePageAsync(page, sort, category, result.Items);
        // Fire-and-forget: persist each item's summary so detail-expand is faster next time.
        _ = MidiShowCache.SaveSummariesAsync(result.Items);
        return result;
    }

    public async Task<MidiShowPageResult> SearchAsync(string query, int page = 1, string sort = "", bool forceRefresh = false, CancellationToken ct = default)
    {
        if (!forceRefresh)
        {
            var cached = MidiShowCache.TryLoadSearchPage(query, page, sort);
            if (cached is not null)
                return cached;
        }

        var result = await _browseClient.SearchAsync(query, page, sort, ct);
        _ = MidiShowCache.SaveSearchPageAsync(query, page, sort, result.Items);
        _ = MidiShowCache.SaveSummariesAsync(result.Items);
        return result;
    }

    public async Task<MidiShowDetails> GetDetailsAsync(MidiShowItem item, CancellationToken ct = default)
    {
        // Check disk cache first.
        var cached = MidiShowCache.TryLoadDetails(item.Id);
        if (cached is not null)
            return cached;

        var details = await _browseClient.GetDetailsAsync(item, ct);
        _ = MidiShowCache.SaveDetailsAsync(details);
        return details;
    }

    #endregion

    #region Download (rotation + failover)

    /// <summary>
    /// Downloads a MIDI, trying accounts in round-robin order and failing over when one is
    /// blocked. Throws <see cref="MidiShowException"/> when nothing succeeds; an
    /// <see cref="MidiShowDownloadError.Unavailable"/> (server-side outage) is rethrown
    /// immediately because rotating accounts cannot help with it.
    /// </summary>
    public async Task<MidiShowDownloadResult> DownloadAsync(string pageUrl, CancellationToken ct = default)
    {
        // Check disk cache first — avoids burning account quota for previously downloaded MIDIs.
        var cachedId = MidiShowCache.ExtractIdFromUrl(pageUrl);
        if (cachedId is not null)
        {
            var cachedData = MidiShowCache.TryLoadMidiFile(cachedId);
            if (cachedData is not null)
            {
                var cachedSummary = MidiShowCache.TryLoadSummary(cachedId);
                var title = cachedSummary?.Title ?? $"MidiShow #{cachedId}";
                Logger.LogStep("MIDISHOW_CACHE_HIT", $"id={cachedId} bytes={cachedData.Length}");
                return new MidiShowDownloadResult(cachedData, title);
            }
        }

        var candidates = OrderedCandidates();
        if (candidates.Count == 0)
            throw new MidiShowException(MidiShowDownloadError.NotAuthenticated,
                "Add a MidiShow account to download. All your accounts are currently limited or signed out.");

        MidiShowException? lastError = null;

        foreach (var entry in candidates)
        {
            ct.ThrowIfCancellationRequested();

            if (!await EnsureLoggedInAsync(entry, ct))
            {
                lastError = new MidiShowException(MidiShowDownloadError.NotAuthenticated,
                    $"Could not sign in account \"{entry.Account.Username}\".");
                RaiseChanged();
                continue;
            }

            try
            {
                var result = await entry.Client!.DownloadAsync(pageUrl, ct);
                MarkActive(entry);
                AdvanceRotation();
                if (cachedId is not null)
                    _ = MidiShowCache.SaveMidiFileAsync(cachedId, result.Data);
                return result;
            }
            catch (MidiShowException ex)
            {
                switch (ex.Reason)
                {
                    // Server-side outage: every account would hit it — don't burn through the pool.
                    case MidiShowDownloadError.Unavailable:
                        throw;

                    // Track-specific problems: rotating accounts won't change the outcome.
                    case MidiShowDownloadError.NotFound:
                    case MidiShowDownloadError.Decode:
                        throw;

                    case MidiShowDownloadError.LimitReached:
                        CoolDown(entry, MidiShowAccountState.Limited, LimitCooldown);
                        lastError = ex;
                        break;

                    case MidiShowDownloadError.RiskControlled:
                        CoolDown(entry, MidiShowAccountState.RiskControlled, RiskCooldown);
                        lastError = ex;
                        break;

                    case MidiShowDownloadError.NotAuthenticated:
                        // Session went stale: try a single fresh login, then one more attempt.
                        entry.LoggedIn = false;
                        if (await EnsureLoggedInAsync(entry, ct))
                        {
                            try
                            {
                                var retry = await entry.Client!.DownloadAsync(pageUrl, ct);
                                MarkActive(entry);
                                AdvanceRotation();
                                if (cachedId is not null)
                                    _ = MidiShowCache.SaveMidiFileAsync(cachedId, retry.Data);
                                return retry;
                            }
                            catch (MidiShowException ex2)
                            {
                                // A global/track-specific failure on the retry is not this
                                // account's fault — surface it instead of burning the pool.
                                if (ex2.Reason is MidiShowDownloadError.Unavailable
                                    or MidiShowDownloadError.NotFound
                                    or MidiShowDownloadError.Decode)
                                    throw;

                                if (ex2.Reason == MidiShowDownloadError.LimitReached)
                                    CoolDown(entry, MidiShowAccountState.Limited, LimitCooldown);
                                else if (ex2.Reason == MidiShowDownloadError.RiskControlled)
                                    CoolDown(entry, MidiShowAccountState.RiskControlled, RiskCooldown);
                                else
                                    MarkAuthFailed(entry);
                                lastError = ex2;
                            }
                        }
                        else
                        {
                            MarkAuthFailed(entry);
                            lastError = ex;
                        }
                        break;

                    default:
                        // Network or unknown: likely global, surface it.
                        throw;
                }

                RaiseChanged();
            }
        }

        throw lastError ?? new MidiShowException(MidiShowDownloadError.LimitReached,
            "All of your MidiShow accounts are currently limited. Add another account or try again later.");
    }

    #endregion

    #region Helpers

    private async Task<bool> EnsureLoggedInAsync(Entry entry, CancellationToken ct)
    {
        if (entry is { LoggedIn: true, Client: not null })
            return true;

        entry.State = MidiShowAccountState.SigningIn;
        RaiseChanged();

        entry.Client ??= new MidiShowClient();
        try
        {
            var ok = entry.Account.IsCookieBased
                ? await entry.Client.LoginByCookies(entry.Account.Cookies!, ct)
                : await entry.Client.LoginAsync(entry.Account.Username, entry.Account.Password ?? string.Empty, ct);

            entry.LoggedIn = ok;
            entry.State = ok ? MidiShowAccountState.Active : MidiShowAccountState.AuthFailed;
            return ok;
        }
        catch (Exception ex)
        {
            Logger.LogException(ex);
            entry.LoggedIn = false;
            entry.State = MidiShowAccountState.AuthFailed;
            return false;
        }
    }

    private List<Entry> OrderedCandidates()
    {
        lock (_lock)
        {
            if (_entries.Count == 0)
                return new List<Entry>();

            var start = _rotation % _entries.Count;
            var ordered = new List<Entry>(_entries.Count);
            for (var i = 0; i < _entries.Count; i++)
                ordered.Add(_entries[(start + i) % _entries.Count]);

            // Skip accounts still inside a cooldown window.
            return ordered.Where(e => EffectiveState(e) != MidiShowAccountState.Limited
                                      && EffectiveState(e) != MidiShowAccountState.RiskControlled).ToList();
        }
    }

    private void AdvanceRotation()
    {
        lock (_lock)
        {
            if (_entries.Count > 0)
                _rotation = (_rotation + 1) % _entries.Count;
        }
    }

    private void MarkActive(Entry entry)
    {
        entry.State = MidiShowAccountState.Active;
        entry.CooldownUntilUtc = DateTime.MinValue;
        RaiseChanged();
    }

    private void MarkAuthFailed(Entry entry)
    {
        entry.LoggedIn = false;
        entry.State = MidiShowAccountState.AuthFailed;
        RaiseChanged();
    }

    private void CoolDown(Entry entry, MidiShowAccountState state, TimeSpan duration)
    {
        entry.State = state;
        entry.CooldownUntilUtc = DateTime.UtcNow + duration;
        Logger.LogStep("MIDISHOW_ACCOUNT_COOLDOWN", $"user={entry.Account.Username} state={state} untilUtc={entry.CooldownUntilUtc:o}");
    }

    private bool ContainsAccount(string username)
    {
        lock (_lock)
            return _entries.Any(e => string.Equals(e.Account.Username, username, StringComparison.Ordinal));
    }

    private void AddEntry(Entry entry)
    {
        lock (_lock)
        {
            _entries.Add(entry);
            Persist();
        }
        RaiseChanged();
    }

    private List<Entry> SnapshotEntries()
    {
        lock (_lock)
            return _entries.ToList();
    }

    // Caller must hold _lock.
    private void Persist() => MidiShowAccountStore.Save(_entries.Select(e => e.Account));

    #endregion

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var entry in _entries)
                entry.Client?.Dispose();
            _entries.Clear();
        }
        _browseClient.Dispose();
    }
}
