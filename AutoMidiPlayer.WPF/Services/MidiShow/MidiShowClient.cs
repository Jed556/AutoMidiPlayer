using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AutoMidiPlayer.Data;

namespace AutoMidiPlayer.WPF.Services.MidiShow;

/// <summary>
/// HTTP client for browsing, searching and downloading MIDI files from midishow.com.
///
/// MidiShow requires a logged-in account to download, and serves the actual MIDI bytes
/// in an obfuscated form (three base64 segments encoded with a per-request custom
/// alphabet derived from an ETag header). This client replicates the community
/// download flow used by self-host downloaders, authenticating with the current
/// user's own MidiShow credentials.
/// </summary>
public sealed class MidiShowClient : IDisposable
{
    private const string Base = "https://www.midishow.com";
    private const string AssetHost = "https://s.midishow.net";

    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";

    private const string StandardBase64Alphabet =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/=";

    private HttpClient _http = null!;
    private CookieContainer _cookies = null!;

    // Politeness / anti-bot hygiene: serialize all requests through one gate and pace them with
    // a token bucket so the client behaves like a human in a browser. A small burst is allowed
    // instantly (so first-open = csrf+login+listing stays snappy), then SUSTAINED activity is
    // throttled to ~1 request per MinRequestInterval — which is what actually looks like a bot.
    // Also lets us honor 429/Retry-After.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromMilliseconds(750);
    private const int BurstCapacity = 4;   // instant requests before pacing kicks in (refills over time)
    private double _tokens = BurstCapacity;
    private DateTime _lastRefillUtc = DateTime.MinValue;
    private const int MaxJitterMs = 400;
    private const int MaxRetries = 3;
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(30);

    // Light per-session cache for listing/detail HTML so re-visiting a page (e.g. paging back
    // and forth) doesn't re-hit the server. Download payloads are NEVER cached (per-request).
    private readonly Dictionary<string, (string Html, DateTime At)> _pageCache = new();
    private static readonly TimeSpan PageCacheTtl = TimeSpan.FromMinutes(5);

    public bool IsAuthenticated { get; private set; }

    /// <summary>Last login attempt diagnostic (shown to the user / logged to help debugging).</summary>
    public string LastDiagnostic { get; private set; } = string.Empty;

    public MidiShowClient()
    {
        ResetSession();
    }

    /// <summary>
    /// Recreates the HttpClient with a brand-new cookie container. Login must run on a
    /// pristine session: simply expiring cookies left over from anonymous browsing is not
    /// enough — the stale CSRF state makes the login POST silently fail. A fresh session
    /// (login as the first request) authenticates reliably.
    /// </summary>
    private void ResetSession()
    {
        _http?.Dispose();
        _cookies = new CookieContainer();

        // Cached pages are session/auth-specific — drop them when the session is rebuilt.
        lock (_pageCache)
            _pageCache.Clear();

        var handler = new HttpClientHandler
        {
            CookieContainer = _cookies,
            UseCookies = true,
            // MidiShow 302-redirects several GETs (e.g. the login page to a locale path);
            // follow them so GETs land on a 200. Login success is detected via the auth cookie.
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
        };

        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(40)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        // NOTE: Do NOT send an Accept-Language header. MidiShow responds to it by setting a
        // "_language" cookie that breaks the login CSRF flow, so the login POST is silently
        // rejected (no _identity cookie). All endpoints use explicit /en/ paths anyway.
    }

    #region Authentication

    /// <summary>
    /// Logs in with the supplied MidiShow credentials. Returns true on success.
    /// </summary>
    public async Task<bool> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        try
        {
            // Start from a brand-new session. Cookies left over from anonymous browsing
            // (e.g. a _csrf cookie set by the listing pages) make the login page's CSRF
            // token fail to validate, so the POST silently returns the form again.
            ResetSession();

            var loginUrl = $"{Base}/user/account/login";
            var csrf = await GetCsrfTokenAsync(loginUrl, ct);

            var form = new Dictionary<string, string>
            {
                ["_csrf"] = csrf,
                ["LoginForm[identity]"] = username,
                ["LoginForm[password]"] = password,
                ["login-button"] = ""
            };

            using var response = await SendAsync(() =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, loginUrl)
                {
                    Content = new FormUrlEncodedContent(form)
                };
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded")
                {
                    CharSet = "UTF-8"
                };
                request.Headers.TryAddWithoutValidation("Origin", Base);
                request.Headers.Referrer = new Uri(loginUrl);
                AddAcceptHeaders(request, xhr: false);
                return request;
            }, ct);

            // A successful login sets the "_identity" auth cookie (the post redirects to the
            // home page, which AllowAutoRedirect follows, so we can't rely on a 302 status).
            IsAuthenticated = HasAuthCookie();

            var cookieNames = string.Join(",", _cookies.GetCookies(new Uri(Base)).Select(c => c.Name));
            LastDiagnostic = $"csrf={csrf.Length} http={(int)response.StatusCode} cookies=[{cookieNames}] auth={IsAuthenticated}";
            Logger.LogStep("MIDISHOW_LOGIN_RESULT", $"userLen={username.Length} passLen={password.Length} | {LastDiagnostic}");

            return IsAuthenticated;
        }
        catch (Exception ex) when (ex is not MidiShowException)
        {
            LastDiagnostic = $"exception: {ex.GetType().Name}: {ex.Message}";
            Logger.Log("MidiShow login failed.");
            Logger.LogException(ex);
            throw new MidiShowException(MidiShowDownloadError.Network, "Could not reach MidiShow to sign in.", ex);
        }
    }

    /// <summary>
    /// Signs in by importing a raw cookie header copied from a browser that is already logged
    /// in to MidiShow (e.g. <c>_identity=...; _csrf=...; ...</c>). This is the fallback for
    /// accounts where the password login is blocked by a captcha or risk control. The session
    /// is considered authenticated when the MidiShow "_identity" cookie is present.
    /// </summary>
    public Task<bool> LoginByCookies(string cookieHeader, CancellationToken ct = default)
    {
        // Start clean so leftover anonymous cookies don't shadow the imported ones.
        ResetSession();

        var baseUri = new Uri(Base);
        if (!string.IsNullOrWhiteSpace(cookieHeader))
        {
            foreach (var part in cookieHeader.Split(';'))
            {
                var trimmed = part.Trim();
                var eq = trimmed.IndexOf('=');
                if (eq <= 0)
                    continue;

                var name = trimmed[..eq].Trim();
                var value = trimmed[(eq + 1)..].Trim();
                if (name.Length == 0)
                    continue;

                try
                {
                    _cookies.Add(baseUri, new Cookie(name, value));
                }
                catch
                {
                    // Skip a malformed cookie rather than failing the whole import.
                }
            }
        }

        IsAuthenticated = HasAuthCookie();
        var cookieNames = string.Join(",", _cookies.GetCookies(baseUri).Select(c => c.Name));
        LastDiagnostic = $"cookie-login auth={IsAuthenticated} cookies=[{cookieNames}]";
        Logger.LogStep("MIDISHOW_COOKIE_LOGIN", LastDiagnostic);
        return Task.FromResult(IsAuthenticated);
    }

    /// <summary>
    /// Exports the current session cookies as a single header string
    /// (<c>name=value; name2=value2</c>), for copying an authenticated session back out.
    /// </summary>
    public string ExportCookies()
    {
        var sb = new StringBuilder();
        foreach (Cookie cookie in _cookies.GetCookies(new Uri(Base)))
        {
            if (cookie.Expired || string.IsNullOrEmpty(cookie.Value))
                continue;
            if (sb.Length > 0)
                sb.Append("; ");
            sb.Append(cookie.Name).Append('=').Append(cookie.Value);
        }
        return sb.ToString();
    }

    public void SignOut()
    {
        IsAuthenticated = false;
        ResetSession();
    }

    /// <summary>True when the MidiShow authentication cookie is present in the session.</summary>
    private bool HasAuthCookie()
    {
        foreach (Cookie cookie in _cookies.GetCookies(new Uri(Base)))
        {
            if (cookie.Name == "_identity" && !cookie.Expired && !string.IsNullOrEmpty(cookie.Value))
                return true;
        }

        return false;
    }

    #endregion

    #region Browse / Search

    /// <summary>
    /// Browses the public MIDI listing. <paramref name="sortByMarks"/> sorts by rating
    /// instead of newest.
    /// </summary>
    public async Task<MidiShowPageResult> BrowseAsync(int page = 1, string sort = "", string category = "", CancellationToken ct = default)
    {
        var url = string.IsNullOrEmpty(category)
            ? $"{Base}/en/midi?page={Math.Max(1, page)}"
            : $"{Base}/en/midi/browse/{category}?page={Math.Max(1, page)}";
        if (!string.IsNullOrEmpty(sort))
            url += "&sort=" + sort;

        var html = await GetStringAsync(url, referer: $"{Base}/en", cache: true, ct: ct);
        return ParseItems(html);
    }

    /// <summary>
    /// Searches MidiShow by keyword (track name, uploader, etc.).
    /// </summary>
    public async Task<MidiShowPageResult> SearchAsync(string query, int page = 1, string sort = "", CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return await BrowseAsync(page, sort, "", ct);

        var url = $"{Base}/en/search/result?q={Uri.EscapeDataString(query.Trim())}&page={Math.Max(1, page)}";
        if (!string.IsNullOrEmpty(sort))
            url += "&sort=" + sort;

        var html = await GetStringAsync(url, referer: $"{Base}/en", cache: true, ct: ct);
        return ParseItems(html);
    }

    /// <summary>
    /// Fetches and parses the full detail page for a MIDI (duration, BPM, tracks, notes,
    /// instruments, rating, description). Does not require authentication.
    /// </summary>
    public async Task<MidiShowDetails> GetDetailsAsync(MidiShowItem item, CancellationToken ct = default)
    {
        string html;
        try
        {
            html = await GetStringAsync(item.PageUrl, referer: $"{Base}/", cache: true, ct: ct);
        }
        catch (Exception ex) when (ex is not MidiShowException)
        {
            throw new MidiShowException(MidiShowDownloadError.Network, "Could not load the MIDI details.", ex);
        }

        return ParseDetails(html, item);
    }

    #endregion

    #region Download

    /// <summary>
    /// Downloads and de-obfuscates the MIDI file for the given detail-page URL.
    /// Requires an authenticated session.
    /// </summary>
    public async Task<MidiShowDownloadResult> DownloadAsync(string pageUrl, CancellationToken ct = default)
    {
        string pageHtml;
        try
        {
            pageHtml = await GetStringAsync(pageUrl, referer: $"{Base}/", ct: ct);
        }
        catch (Exception ex) when (ex is not MidiShowException)
        {
            throw new MidiShowException(MidiShowDownloadError.Network, "Could not load the MIDI page.", ex);
        }

        var title = ExtractTitle(pageHtml);
        var csrf = ExtractMeta(pageHtml, "csrf-token");

        var trackNames = new Dictionary<int, string>();
        var tracksTableMatch = Regex.Match(pageHtml, "<h2[^>]*>Tracks</h2>.*?<tbody>(.*?)</tbody>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (tracksTableMatch.Success)
        {
            var tbody = tracksTableMatch.Groups[1].Value;
            var rows = Regex.Matches(tbody, "<tr>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            foreach (Match row in rows)
            {
                var cols = Regex.Matches(row.Groups[1].Value, "<td[^>]*>(.*?)</td>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                if (cols.Count >= 4)
                {
                    var trackIdxStr = CleanText(cols[0].Groups[1].Value);
                    if (int.TryParse(trackIdxStr, out var trackIdx))
                    {
                        var instruments = new List<string>();
                        var smalls = Regex.Matches(cols[3].Groups[1].Value, "<small[^>]*>(.*?)</small>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                        foreach (Match small in smalls)
                        {
                            var inst = CleanText(small.Groups[1].Value);
                            if (!string.IsNullOrWhiteSpace(inst))
                                instruments.Add(inst);
                        }
                        
                        if (instruments.Count > 0)
                            trackNames[trackIdx] = string.Join(", ", instruments);
                    }
                }
            }
        }

        var midElement = Regex.Match(pageHtml, "<[^>]*\\bdata-mid=\"(?<mid>[^\"]+)\"[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!midElement.Success)
            throw new MidiShowException(MidiShowDownloadError.NotFound, "This page does not expose a downloadable MIDI.");

        var fakeMidiUrl = WebUtility.HtmlDecode(midElement.Groups["mid"].Value);

        // The MIDI id lives on the same element as data-mid; fall back to the URL.
        var id = Regex.Match(midElement.Value, "\\bdata-id=\"(?<id>\\d+)\"", RegexOptions.IgnoreCase).Groups["id"].Value;
        if (string.IsNullOrEmpty(id))
            id = Regex.Match(pageUrl, "(?<id>\\d+)(?:\\.html)?$").Groups["id"].Value;
        if (string.IsNullOrEmpty(id))
            throw new MidiShowException(MidiShowDownloadError.NotFound, "Could not determine the MIDI id.");

        // First request: the obfuscated head segments + the ETag-derived alphabet tail.
        string text1;
        string etag;
        {
            using var response1 = await SendAsync(() =>
            {
                var request1 = new HttpRequestMessage(HttpMethod.Post, $"{Base}/midi/new-file?id={id}")
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["id"] = id })
                };
                request1.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded")
                {
                    CharSet = "UTF-8"
                };
                request1.Headers.TryAddWithoutValidation("Origin", Base);
                request1.Headers.Referrer = new Uri(pageUrl);
                request1.Headers.TryAddWithoutValidation("X-Csrf-Token", csrf);
                request1.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
                AddAcceptHeaders(request1, xhr: true);
                return request1;
            }, ct);

            if (response1.StatusCode == HttpStatusCode.Forbidden)
            {
                var errorBody = await response1.Content.ReadAsStringAsync(ct);
                Logger.Log($"MidiShow returned 403 Forbidden. Body: {errorBody}");
                throw ClassifyForbidden(errorBody);
            }

            if (!response1.IsSuccessStatusCode)
                throw new MidiShowException(MidiShowDownloadError.Network,
                    $"MidiShow returned {(int)response1.StatusCode} while preparing the download.");

            var bytes1 = await response1.Content.ReadAsByteArrayAsync(ct);
            text1 = Encoding.UTF8.GetString(bytes1);
            etag = ReadETag(response1);
        }

        if (text1.Length < 56)
            throw new MidiShowException(MidiShowDownloadError.Decode, "Unexpected response from MidiShow (segment too short).");

        // Second request: the middle segment, served from the asset host as a ".js" payload.
        var assetUrl = BuildAssetUrl(fakeMidiUrl);

        string text2;
        try
        {
            text2 = await GetStringAsync(assetUrl, referer: $"{Base}/", xhr: true, ct: ct);
        }
        catch (Exception ex) when (ex is not MidiShowException)
        {
            throw new MidiShowException(MidiShowDownloadError.Network, "Could not download the MIDI data segment.", ex);
        }

        if (text2.Length < 6)
            throw new MidiShowException(MidiShowDownloadError.Decode, "Unexpected MIDI data segment from MidiShow.");

        try
        {
            // Reconstruct the per-request base64 alphabet, then decode + concatenate the
            // three segments in the order used by MidiShow's own player.
            var alphabet = Hex2Str(etag) + text1[56..];

            byte[] segmentA = DecodeBase64(text1.Substring(28, 28), alphabet);
            byte[] segmentB = DecodeBase64(text2[3..^3], alphabet);
            byte[] segmentC = DecodeBase64(text1[..28], alphabet);

            var midi = new byte[segmentA.Length + segmentB.Length + segmentC.Length];
            Buffer.BlockCopy(segmentA, 0, midi, 0, segmentA.Length);
            Buffer.BlockCopy(segmentB, 0, midi, segmentA.Length, segmentB.Length);
            Buffer.BlockCopy(segmentC, 0, midi, segmentA.Length + segmentB.Length, segmentC.Length);

            return new MidiShowDownloadResult(midi, title, trackNames.Count > 0 ? trackNames : null);
        }
        catch (MidiShowException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to decode MidiShow MIDI payload.");
            Logger.LogException(ex);
            throw new MidiShowException(MidiShowDownloadError.Decode, "Could not decode the downloaded MIDI file.", ex);
        }
    }

    #endregion

    /// <summary>
    /// MidiShow answers the download endpoint with 403 in several unrelated situations, and the
    /// (usually Chinese) body is what tells them apart:
    ///   * the MIDI download/preview feature being temporarily turned off server-side as an
    ///     anti-scraping measure — e.g. "由于近期存在大量恶意抓取，MIDI试听功能已暂时关闭，
    ///     您可先通过OGG方式试听部分曲目。" This hits every user; signing in again does NOT help;
    ///   * a per-account download quota / points / VIP requirement;
    ///   * a genuinely unauthenticated request (the only case that warrants a re-login).
    /// Reporting the first two as "session expired" (and clearing the saved login, as the old
    /// catch-all NotAuthenticated did) is both wrong and a dead end for the user, so each gets
    /// its own reason that leaves the session intact.
    /// </summary>
    private static MidiShowException ClassifyForbidden(string body)
    {
        body ??= string.Empty;
        bool Has(params string[] needles) =>
            needles.Any(n => body.Contains(n, StringComparison.OrdinalIgnoreCase));

        // Feature temporarily disabled server-side (anti-scraping). Match the distinctive bits of
        // the Chinese notice, plus English fallbacks in case the locale/wording changes.
        if (Has("恶意抓取", "暂时关闭", "试听", "OGG", "scraping", "temporarily disabled"))
            return new MidiShowException(MidiShowDownloadError.Unavailable,
                "MidiShow has temporarily disabled MIDI downloads on their side (an anti-scraping " +
                "measure). This is server-side and affects everyone — please try again later. " +
                "Browsing and search still work.");

        // Risk control / abnormal-activity flag on the account. 风控 = risk control,
        // 异常 = abnormal, 频繁 = frequent, 验证 = (extra) verification. The account is valid but
        // temporarily blocked — surfaced separately so the pool can rotate to another account.
        if (Has("风控", "异常", "频繁", "risk control", "abnormal", "too frequent", "unusual activity"))
            return new MidiShowException(MidiShowDownloadError.RiskControlled,
                "MidiShow flagged this account's activity as abnormal (risk control). Try a " +
                "different account, or wait a while before downloading again.");

        // Per-account quota / points / VIP. English keywords plus common Chinese equivalents:
        // 积分 = points, 次数 = (download) count, 限制 = limit, 会员/VIP = membership, 余额 = balance.
        if (Has("limit", "points", "insufficient", "vip", "积分", "次数", "限制", "会员", "余额"))
            return new MidiShowException(MidiShowDownloadError.LimitReached,
                "Your MidiShow download limit was reached, or this track needs more points / VIP on " +
                "your account. Your sign-in is still valid.");

        // Anything else: treat as a real authentication problem (re-login is the right fix).
        return new MidiShowException(MidiShowDownloadError.NotAuthenticated,
            "MidiShow requires you to be signed in to download.");
    }

    #region HTML / payload helpers

    private async Task<string> GetStringAsync(string url, string? referer = null, bool xhr = false, bool cache = false, CancellationToken ct = default)
    {
        if (cache)
        {
            lock (_pageCache)
            {
                if (_pageCache.TryGetValue(url, out var entry) && DateTime.UtcNow - entry.At < PageCacheTtl)
                    return entry.Html;
            }
        }

        using var response = await SendAsync(() =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (referer is not null)
                request.Headers.Referrer = new Uri(referer);
            AddAcceptHeaders(request, xhr);
            return request;
        }, ct);

        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var html = System.Text.Encoding.UTF8.GetString(bytes);

        if (cache)
        {
            lock (_pageCache)
                _pageCache[url] = (html, DateTime.UtcNow);
        }

        return html;
    }

    /// <summary>
    /// Sends a request through the throttle gate. Requests are serialized and spaced out by a
    /// small randomized interval (human-like pacing, no bursts), and 429/503 responses are
    /// retried with backoff honoring the server's Retry-After. The factory is re-invoked per
    /// attempt because an <see cref="HttpRequestMessage"/> (and its content) can only be sent once.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> requestFactory, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                await DelayForPacingAsync(ct);

                using var request = requestFactory();
                var response = await _http.SendAsync(request, ct);

                var throttled = response.StatusCode == HttpStatusCode.TooManyRequests
                                || response.StatusCode == HttpStatusCode.ServiceUnavailable;

                if (throttled && attempt < MaxRetries)
                {
                    var wait = GetRetryAfter(response) ?? TimeSpan.FromSeconds(2 * attempt);
                    if (wait > MaxRetryAfter) wait = MaxRetryAfter;
                    response.Dispose();
                    Logger.LogStep("MIDISHOW_BACKOFF", $"status={(int)response.StatusCode} attempt={attempt} waitMs={(int)wait.TotalMilliseconds}");
                    await Task.Delay(wait, ct);
                    continue;
                }

                return response;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Token-bucket pacing. A burst of up to <see cref="BurstCapacity"/> requests passes with no
    /// delay (keeps first-open and quick interactions snappy); tokens refill at one per
    /// <see cref="MinRequestInterval"/>. Once the burst is spent, sustained requests wait for the
    /// next token (plus a little jitter). Called inside the serialized gate, so token state is
    /// only ever touched by one request at a time.
    /// </summary>
    private async Task DelayForPacingAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        if (_lastRefillUtc == DateTime.MinValue)
            _lastRefillUtc = now;

        // Refill tokens proportionally to the time elapsed since the last refill.
        var refill = (now - _lastRefillUtc).TotalMilliseconds / MinRequestInterval.TotalMilliseconds;
        if (refill > 0)
        {
            _tokens = Math.Min(BurstCapacity, _tokens + refill);
            _lastRefillUtc = now;
        }

        if (_tokens >= 1)
        {
            _tokens -= 1;
            return; // within burst allowance — no delay
        }

        // Bucket empty: wait for (most of) one token to refill, plus jitter.
        var waitMs = (1 - _tokens) * MinRequestInterval.TotalMilliseconds + Random.Shared.Next(0, MaxJitterMs);
        await Task.Delay(TimeSpan.FromMilliseconds(waitMs), ct);
        _tokens = 0;
        _lastRefillUtc = DateTime.UtcNow;
    }

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is null)
            return null;

        if (retryAfter.Delta is TimeSpan delta)
            return delta > TimeSpan.Zero ? delta : TimeSpan.Zero;

        if (retryAfter.Date is DateTimeOffset date)
        {
            var diff = date - DateTimeOffset.UtcNow;
            return diff > TimeSpan.Zero ? diff : TimeSpan.Zero;
        }

        return null;
    }

    /// <summary>Adds a browser-like Accept header (document navigations vs. XHR/asset fetches).</summary>
    private static void AddAcceptHeaders(HttpRequestMessage request, bool xhr)
    {
        request.Headers.TryAddWithoutValidation("Accept", xhr
            ? "*/*"
            : "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
    }

    private async Task<string> GetCsrfTokenAsync(string pageUrl, CancellationToken ct)
    {
        var html = await GetStringAsync(pageUrl, ct: ct);
        var token = ExtractMeta(html, "csrf-token");
        if (string.IsNullOrEmpty(token))
            throw new MidiShowException(MidiShowDownloadError.Network, "MidiShow did not return a CSRF token.");
        return token;
    }

    private static string ExtractMeta(string html, string name)
    {
        var match = Regex.Match(html,
            $"<meta[^>]*name=\"{Regex.Escape(name)}\"[^>]*content=\"(?<v>[^\"]*)\"",
            RegexOptions.IgnoreCase);
        if (match.Success)
            return WebUtility.HtmlDecode(match.Groups["v"].Value);

        // attribute order may be reversed
        match = Regex.Match(html,
            $"<meta[^>]*content=\"(?<v>[^\"]*)\"[^>]*name=\"{Regex.Escape(name)}\"",
            RegexOptions.IgnoreCase);
        return match.Success ? WebUtility.HtmlDecode(match.Groups["v"].Value) : string.Empty;
    }

    private static string ExtractTitle(string html)
    {
        // Prefer the title inside the player container, else the first <h1>.
        var container = Regex.Match(html, "ms-player-container", RegexOptions.IgnoreCase);
        if (container.Success)
        {
            var slice = html.Substring(container.Index, Math.Min(600, html.Length - container.Index));
            var h1 = Regex.Match(slice, "<h1[^>]*>(?<t>.*?)</h1>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (h1.Success)
                return CleanText(h1.Groups["t"].Value);
        }

        var anyH1 = Regex.Match(html, "<h1[^>]*>(?<t>.*?)</h1>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return anyH1.Success ? CleanText(anyH1.Groups["t"].Value) : "MidiShow track";
    }

    private static string ReadETag(HttpResponseMessage response)
    {
        string raw = response.Headers.ETag?.Tag ?? string.Empty;
        if (string.IsNullOrEmpty(raw) && response.Headers.TryGetValues("ETag", out var values))
            raw = values.FirstOrDefault() ?? string.Empty;

        raw = raw.Trim();
        if (raw.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
            raw = raw[2..];
        return raw.Trim('"');
    }

    private static MidiShowPageResult ParseItems(string html)
    {
        var items = new List<MidiShowItem>();
        var seen = new HashSet<string>();

        var statusMatch = Regex.Match(html, @"Showing\s+(?<range>[\d,-]+)\s+of\s+(?<total>[\d,]+)", RegexOptions.IgnoreCase);
        string statusText = statusMatch.Success ? $"Showing {statusMatch.Groups["range"].Value}/{statusMatch.Groups["total"].Value}" : "";

        // Each result is `<div data-key="ID"> <a ... href="..."> ... </a>`.
        foreach (Match match in Regex.Matches(html,
                     "<div\\s+data-key=\"(?<id>\\d+)\">\\s*<a[^>]*href=\"(?<href>[^\"]+)\"[^>]*>(?<body>.*?)</a>",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var id = match.Groups["id"].Value;
            if (!seen.Add(id))
                continue;

            var href = WebUtility.HtmlDecode(match.Groups["href"].Value);
            if (href.StartsWith("/", StringComparison.Ordinal))
                href = Base + href;

            var body = match.Groups["body"].Value;

            var title = CleanText(Regex.Match(body, "<h3[^>]*>(?<t>.*?)</h3>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline).Groups["t"].Value);
            if (string.IsNullOrWhiteSpace(title))
                title = $"MidiShow #{id}";

            var img = Regex.Match(body, "<img[^>]*>", RegexOptions.IgnoreCase);
            string? uploader = null;
            string? thumb = null;
            if (img.Success)
            {
                var alt = Regex.Match(img.Value, "alt=\"(?<v>[^\"]*)\"", RegexOptions.IgnoreCase);
                if (alt.Success && !string.IsNullOrWhiteSpace(alt.Groups["v"].Value))
                    uploader = WebUtility.HtmlDecode(alt.Groups["v"].Value);

                var src = Regex.Match(img.Value, "src=\"(?<v>[^\"]+)\"", RegexOptions.IgnoreCase);
                if (src.Success)
                    thumb = WebUtility.HtmlDecode(src.Groups["v"].Value);
            }

            var std = Regex.Match(body, "midi-std-(?<v>[A-Za-z0-9]+)", RegexOptions.IgnoreCase);

            // Description: the <p class="... text-body ..."> snippet, cleaned to one tidy line
            // (drop bare URLs and collapse line breaks so it doesn't sprawl over several rows).
            var descMatch = Regex.Match(body, "<p[^>]*text-body[^>]*>(?<v>.*?)</p>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var description = CleanText(descMatch.Groups["v"].Value);
            description = Regex.Replace(description, "https?://\\S+", "");
            description = Regex.Replace(description, "\\s+", " ").Trim();

            // Category: the primary badge; Tags: the remaining badges.
            var category = CleanText(Regex.Match(body, "<span[^>]*badge-primary[^>]*>(?<v>.*?)</span>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline).Groups["v"].Value);

            var tags = new List<string>();
            foreach (Match t in Regex.Matches(body, "<span class=\"badge\"[^>]*>(?<v>.*?)</span>",
                         RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                var tag = CleanText(t.Groups["v"].Value);
                if (!string.IsNullOrWhiteSpace(tag))
                    tags.Add(tag);
            }

            // Rating value + count.
            var rating = Regex.Match(body, "font-weight-semi-bold\">\\s*(?<v>[\\d.]+)", RegexOptions.IgnoreCase).Groups["v"].Value;
            var ratingCount = Regex.Match(body, "(?<v>\\d+)\\s+ratings?", RegexOptions.IgnoreCase).Groups["v"].Value;

            // Extract file size (e.g., 29.73 KB)
            var fileSize = Regex.Match(body, "(?<v>[\\d.]+\\s*[KMG]B)", RegexOptions.IgnoreCase).Groups["v"].Value;

            var trackCount = ExtractTitled(body, "Tracks");
            if (string.IsNullOrEmpty(trackCount)) trackCount = "0";

            var instruments = ExtractTitled(body, "Instrument count");
            if (string.IsNullOrEmpty(instruments)) instruments = "0";

            var finalFileSize = ExtractTitled(body, "File size") ?? fileSize;

            var uploadDate = Regex.Match(body, @"Uploaded on\s+(?<v>[A-Za-z]+\s+\d{1,2},\s+\d{4})", RegexOptions.IgnoreCase).Groups["v"].Value;

            items.Add(new MidiShowItem
            {
                Id = id,
                PageUrl = href,
                Title = title,
                Uploader = uploader,
                UploadDate = string.IsNullOrEmpty(uploadDate) ? null : uploadDate,
                ThumbnailUrl = thumb,
                Standard = std.Success ? std.Groups["v"].Value.ToUpperInvariant() : null,
                Duration = ExtractTitled(body, "Duration") ?? Regex.Match(body, "(?<v>\\d{2}:\\d{2})").Groups["v"].Value,
                TrackCount = trackCount,
                InstrumentCount = instruments,
                FileSize = string.IsNullOrEmpty(finalFileSize) ? null : finalFileSize,
                Downloads = ExtractDownloads(body) ?? "0",
                Category = category,
                Tags = string.Join(" · ", tags),
                TagsList = tags,
                Description = description,
                Rating = string.IsNullOrEmpty(rating) ? "0.0" : rating,
                RatingCount = string.IsNullOrEmpty(ratingCount) ? "0" : ratingCount
            });
        }

        return new MidiShowPageResult(items, statusText);
    }

    /// <summary>Reads the value after an icon for a <c>title="Label"</c> stat cell.</summary>
    private static string? ExtractTitled(string html, string label)
    {
        var m = Regex.Match(html,
            $"title=\"{Regex.Escape(label)}\"[^>]*>\\s*<i[^>]*></i>\\s*(?<v>[^<]+)",
            RegexOptions.IgnoreCase);
        if (!m.Success)
            return null;
        var v = WebUtility.HtmlDecode(m.Groups["v"].Value).Trim();
        return string.IsNullOrEmpty(v) ? null : v;
    }

    private static string? ExtractDownloads(string html)
    {
        var m = Regex.Match(html, ">\\s*(?<v>[\\d.,]+\\s*[KMkm]?)\\s+downloads\\s*<", RegexOptions.IgnoreCase);
        if (!m.Success)
            return null;
        var v = m.Groups["v"].Value.Replace(" ", "").Trim();
        return string.IsNullOrEmpty(v) ? null : v;
    }

    private static MidiShowDetails ParseDetails(string html, MidiShowItem item)
    {
        var text = WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(html, "<[^>]+>", " "), "\\s+", " "));

        // JSON-LD abstract (auto summary with file size + instruments) and rating.
        var abstractText = UnescapeJson(Regex.Match(html, "\"abstract\"\\s*:\\s*\"(?<v>(?:[^\"\\\\]|\\\\.)*)\"").Groups["v"].Value);
        var rating = Regex.Match(html, "\"ratingValue\"\\s*:\\s*\"?(?<v>[\\d.]+)").Groups["v"].Value;
        var ratingCount = Regex.Match(html, "\"ratingCount\"\\s*:\\s*\"?(?<v>\\d+)").Groups["v"].Value;

        // Duration from JSON-LD ISO 8601 (PT06M14S), else the visible stat.
        string? duration = null;
        var iso = Regex.Match(html, "\"duration\"\\s*:\\s*\"PT(?:(?<h>\\d+)H)?(?<m>\\d+)M(?<s>\\d+)S\"");
        if (iso.Success)
        {
            var h = iso.Groups["h"].Success ? int.Parse(iso.Groups["h"].Value) : 0;
            var mm = int.Parse(iso.Groups["m"].Value);
            var ss = int.Parse(iso.Groups["s"].Value);
            duration = h > 0 ? $"{h}:{mm:D2}:{ss:D2}" : $"{mm:D2}:{ss:D2}";
        }

        string? Find(string pattern)
        {
            var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            return m.Success ? m.Groups["v"].Value.Trim() : null;
        }

        var bpm = Find("\\bBPM\\b[^0-9]{0,6}(?<v>\\d{2,4})") ?? Find("(?<v>\\d{2,4})\\s*bpm");
        // Prefer the detail page's own track count; the list value is often 0/missing.
        var tracks = Find("into (?<v>\\d{1,4}) tracks") ?? Find("\\bTracks\\b[^0-9]{0,6}(?<v>\\d{1,4})");
        if (string.IsNullOrEmpty(tracks) || tracks == "0")
            tracks = item.HasTrackCount ? item.TrackCount : tracks;
        var notes = Find("(?<v>[\\d,]+)\\s+notes");
        var fileSize = Find("file size:\\s*(?<v>[\\d.]+\\s*[KMG]?B)");
        var instruments = Find("played by (?<v>[^.,;]+?)(?:\\.|,| MidiShow|$)");

        // Introduction text (user-written + auto summary), trimmed to a readable length.
        string? intro = null;
        var introIdx = text.IndexOf("Introduction", StringComparison.OrdinalIgnoreCase);
        if (introIdx >= 0)
        {
            var rest = text[(introIdx + "Introduction".Length)..].Trim();

            // Drop bare URLs and cut the trailing SEO boilerplate (ratings dump, tag list, etc.).
            rest = Regex.Replace(rest, "https?://\\S+", "");
            foreach (var stop in new[] { "Channels and Instruments", "Comments", "Related", "Download", "Ratings" })
            {
                var si = rest.IndexOf(stop, StringComparison.OrdinalIgnoreCase);
                if (si > 0)
                    rest = rest[..si];
            }

            rest = Regex.Replace(rest, "\\s+", " ").Trim();
            if (rest.Length > 900)
                rest = rest[..900].TrimEnd() + "…";
            if (!string.IsNullOrWhiteSpace(rest))
                intro = rest;
        }

        var std = Regex.Match(html, "midi-std-(?<v>[A-Za-z0-9]+)", RegexOptions.IgnoreCase);

        var trackList = new List<MidiShowTrack>();
        var tableMatch = Regex.Match(html, "<table[^>]*>.*?<thead>.*?<tr[^>]*>.*?<th[^>]*>#.*?</tr>.*?</thead>.*?<tbody>(.*?)</tbody>.*?</table>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (tableMatch.Success)
        {
            var tbody = tableMatch.Groups[1].Value;
            var rows = Regex.Matches(tbody, "<tr>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            foreach (Match row in rows)
            {
                var cols = Regex.Matches(row.Groups[1].Value, "<td[^>]*>(.*?)</td>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                if (cols.Count >= 4)
                {
                    var num = CleanText(cols[0].Groups[1].Value);
                    var name = CleanText(cols[1].Groups[1].Value);
                    var channel = CleanText(cols[2].Groups[1].Value);
                    
                    var instHtml = cols[3].Groups[1].Value;
                    var inst = WebUtility.HtmlDecode(Regex.Replace(instHtml, "<[^>]+>", ", ")).Trim().Trim(',');
                    inst = Regex.Replace(inst, "(^,\\s*)|(,\\s*$)", "").Trim();
                    inst = Regex.Replace(inst, "(,\\s*)+", ", ");

                    if (!string.IsNullOrWhiteSpace(num) && (!string.IsNullOrWhiteSpace(channel) || !string.IsNullOrWhiteSpace(inst)))
                    {
                        trackList.Add(new MidiShowTrack
                        {
                            Number = num,
                            Name = name,
                            Channel = channel,
                            Instrument = inst
                        });
                    }
                }
            }
        }

        var channelsAndInstMatch = Regex.Match(html, "Channels and Instruments.*?<ul class=\"step\">(.*?)</ul>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (channelsAndInstMatch.Success)
        {
            var stepItems = Regex.Matches(channelsAndInstMatch.Groups[1].Value, "<li class=\"step-item\">(.*?)</li>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            foreach (Match mItem in stepItems)
            {
                var content = mItem.Groups[1].Value;
                var progMatch = Regex.Match(content, @"Program ID:\s*(\d+),\s*Track:\s*(\d+)", RegexOptions.IgnoreCase);
                var notesMatch = Regex.Match(content, @"([\d,]+)\s+notes/chords", RegexOptions.IgnoreCase);

                if (progMatch.Success)
                {
                    var trackNum = progMatch.Groups[2].Value;
                    var progId = progMatch.Groups[1].Value;
                    var trackNotes = notesMatch.Success ? notesMatch.Groups[1].Value : "";

                    var track = trackList.FirstOrDefault(t => t.Number == trackNum);
                    if (track != null)
                    {
                        track.ProgramId = progId;
                        track.NotesCount = trackNotes;
                    }
                }
            }
        }

        return new MidiShowDetails
        {
            Id = item.Id,
            PageUrl = item.PageUrl,
            Title = ExtractTitle(html),
            Uploader = item.Uploader,
            ThumbnailUrl = item.ThumbnailUrl,
            Standard = std.Success ? std.Groups["v"].Value.ToUpperInvariant() : item.Standard,
            Duration = duration ?? item.Duration,
            Bpm = bpm,
            TrackCount = tracks,
            NoteCount = notes,
            FileSize = fileSize,
            Instruments = string.IsNullOrWhiteSpace(instruments) ? null : instruments,
            Rating = string.IsNullOrEmpty(rating) || rating == "0" || rating == "0.0"
                ? (item.HasRating ? $"{item.Rating} ({item.RatingCount})" : null)
                : (string.IsNullOrEmpty(ratingCount) ? rating : $"{rating} ({ratingCount})"),
            Introduction = intro ?? (string.IsNullOrWhiteSpace(abstractText) ? null : abstractText),
            Category = item.Category,
            Tags = item.Tags,
            Downloads = item.Downloads,
            Tracks = trackList
        };
    }

    private static string UnescapeJson(string s)
    {
        if (string.IsNullOrEmpty(s))
            return string.Empty;
        return s.Replace("\\\"", "\"").Replace("\\/", "/").Replace("\\n", " ").Replace("\\r", " ").Replace("\\t", " ").Replace("\\\\", "\\").Trim();
    }

    private static string CleanText(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var stripped = Regex.Replace(value, "<.*?>", string.Empty, RegexOptions.Singleline);
        return WebUtility.HtmlDecode(stripped).Trim();
    }

    /// <summary>
    /// Turns the page's <c>data-mid</c> URL into the asset-host URL for the middle segment:
    /// swap the host to the asset CDN and, on the legacy format where the filename ends in
    /// ".mid", rewrite that extension to ".js". On the current format the filename already has
    /// no ".mid" extension, so only the host swap applies (matching MidiShow's own player and
    /// the reference downloader). NOTE: an authenticated page and an anonymous page can emit
    /// different <c>data-mid</c> shapes; do not assume one without testing while signed in.
    /// </summary>
    private static string BuildAssetUrl(string fakeMidiUrl)
    {
        return fakeMidiUrl
            .Replace(Base, AssetHost, StringComparison.OrdinalIgnoreCase)
            .Replace(".mid?", ".js?", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Converts a hex string to text, stopping at a "00" byte (matches the reference tool).
    /// </summary>
    private static string Hex2Str(string hex)
    {
        if (string.IsNullOrEmpty(hex))
            return string.Empty;

        var sb = new StringBuilder(hex.Length / 2);
        for (var i = 0; i + 1 < hex.Length; i += 2)
        {
            var pair = hex.Substring(i, 2);
            if (pair == "00")
                break;

            if (!int.TryParse(pair, System.Globalization.NumberStyles.HexNumber, null, out var code))
                break;

            sb.Append((char)code);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Decodes a base64 string that uses a custom alphabet by translating each character
    /// back to the standard base64 alphabet, then decoding.
    /// </summary>
    private static byte[] DecodeBase64(string encoded, string customAlphabet)
    {
        var map = new Dictionary<char, char>(customAlphabet.Length);
        var count = Math.Min(customAlphabet.Length, StandardBase64Alphabet.Length);
        for (var i = 0; i < count; i++)
            map[customAlphabet[i]] = StandardBase64Alphabet[i];

        var sb = new StringBuilder(encoded.Length);
        foreach (var ch in encoded)
            sb.Append(map.TryGetValue(ch, out var mapped) ? mapped : ch);

        return Convert.FromBase64String(sb.ToString());
    }

    #endregion

    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
    }
}
