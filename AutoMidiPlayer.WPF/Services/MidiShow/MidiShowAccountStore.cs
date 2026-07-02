using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using AutoMidiPlayer.Data;

namespace AutoMidiPlayer.WPF.Services.MidiShow;

/// <summary>
/// A single configured MidiShow account in the download pool. An account is either
/// password-based (<see cref="Password"/> set) or cookie-based (<see cref="Cookies"/> set —
/// a raw "name=value; ..." header copied from a signed-in browser, used when a password
/// login is blocked by a captcha / risk control).
/// </summary>
public sealed record MidiShowAccount(string Username, string? Password, string? Cookies)
{
    /// <summary>True when this account signs in with an imported cookie header, not a password.</summary>
    public bool IsCookieBased => string.IsNullOrEmpty(Password) && !string.IsNullOrEmpty(Cookies);

    public static MidiShowAccount FromPassword(string username, string password) =>
        new(username, password, null);

    public static MidiShowAccount FromCookies(string label, string cookies) =>
        new(label, null, cookies);
}

/// <summary>
/// Persists the user's MidiShow account pool, encrypted with Windows DPAPI scoped to the
/// current user. The encrypted blob can only be decrypted by the same Windows account on the
/// same machine, so credentials never travel with the app. Supersedes the single-account
/// <see cref="MidiShowCredentialStore"/>, which is read once to migrate an existing login.
/// </summary>
public static class MidiShowAccountStore
{
    public static bool HasAccounts => File.Exists(AppPaths.MidiShowAccountsPath);

    /// <summary>
    /// Loads the configured accounts. On first run after the multi-account upgrade, migrates a
    /// legacy single credential (midishow.cred) into the new pool so existing users stay signed in.
    /// </summary>
    public static List<MidiShowAccount> Load()
    {
        try
        {
            if (!File.Exists(AppPaths.MidiShowAccountsPath))
                return MigrateLegacy();

            var protectedBytes = File.ReadAllBytes(AppPaths.MidiShowAccountsPath);
            var json = MidiShowProtectedStorage.Unprotect(protectedBytes);
            if (json is null)
                return new List<MidiShowAccount>();

            var accounts = JsonSerializer.Deserialize<List<MidiShowAccount>>(json);
            return accounts?.Where(a => !string.IsNullOrWhiteSpace(a.Username)).ToList()
                   ?? new List<MidiShowAccount>();
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to load MidiShow accounts.");
            Logger.LogException(ex);
            return new List<MidiShowAccount>();
        }
    }

    public static void Save(IEnumerable<MidiShowAccount> accounts)
    {
        AppPaths.EnsureDirectoryExists();

        var list = accounts.Where(a => !string.IsNullOrWhiteSpace(a.Username)).ToList();
        var json = JsonSerializer.SerializeToUtf8Bytes(list);
        var protectedBytes = MidiShowProtectedStorage.Protect(json);
        File.WriteAllBytes(AppPaths.MidiShowAccountsPath, protectedBytes);
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(AppPaths.MidiShowAccountsPath))
                File.Delete(AppPaths.MidiShowAccountsPath);
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to clear MidiShow accounts.");
            Logger.LogException(ex);
        }
    }

    private static List<MidiShowAccount> MigrateLegacy()
    {
        var legacy = MidiShowCredentialStore.Load();
        if (legacy is null)
            return new List<MidiShowAccount>();

        var migrated = new List<MidiShowAccount> { MidiShowAccount.FromPassword(legacy.Username, legacy.Password) };
        try
        {
            Save(migrated);
            MidiShowCredentialStore.Clear();
            Logger.LogStep("MIDISHOW_ACCOUNTS_MIGRATE", $"migrated 1 legacy account ({legacy.Username.Length} chars)");
        }
        catch (Exception ex)
        {
            Logger.LogException(ex);
        }

        return migrated;
    }
}

/// <summary>
/// Windows DPAPI (CurrentUser) protect/unprotect helpers shared by the MidiShow credential
/// stores. The optional entropy ties the blob to this app.
/// </summary>
internal static class MidiShowProtectedStorage
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AutoMidiPlayer.MidiShow.v1");
    private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool CryptProtectData(
        ref DATA_BLOB pDataIn, string? szDataDescr, ref DATA_BLOB pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool CryptUnprotectData(
        ref DATA_BLOB pDataIn, IntPtr ppszDataDescr, ref DATA_BLOB pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    public static byte[] Protect(byte[] data)
    {
        var inBlob = ToBlob(data);
        var entropyBlob = ToBlob(Entropy);
        var outBlob = new DATA_BLOB();

        try
        {
            if (!CryptProtectData(ref inBlob, null, ref entropyBlob, IntPtr.Zero, IntPtr.Zero,
                    CRYPTPROTECT_UI_FORBIDDEN, ref outBlob))
                throw new InvalidOperationException("CryptProtectData failed.");

            return FromBlob(outBlob);
        }
        finally
        {
            FreeInputBlob(inBlob);
            FreeInputBlob(entropyBlob);
            FreeOutputBlob(outBlob);
        }
    }

    public static byte[]? Unprotect(byte[] data)
    {
        var inBlob = ToBlob(data);
        var entropyBlob = ToBlob(Entropy);
        var outBlob = new DATA_BLOB();

        try
        {
            if (!CryptUnprotectData(ref inBlob, IntPtr.Zero, ref entropyBlob, IntPtr.Zero, IntPtr.Zero,
                    CRYPTPROTECT_UI_FORBIDDEN, ref outBlob))
                return null;

            return FromBlob(outBlob);
        }
        finally
        {
            FreeInputBlob(inBlob);
            FreeInputBlob(entropyBlob);
            FreeOutputBlob(outBlob);
        }
    }

    private static DATA_BLOB ToBlob(byte[] data)
    {
        var blob = new DATA_BLOB { cbData = data.Length, pbData = Marshal.AllocHGlobal(data.Length) };
        Marshal.Copy(data, 0, blob.pbData, data.Length);
        return blob;
    }

    private static byte[] FromBlob(DATA_BLOB blob)
    {
        var result = new byte[blob.cbData];
        Marshal.Copy(blob.pbData, result, 0, blob.cbData);
        return result;
    }

    // Input/entropy blobs are allocated by us with AllocHGlobal.
    private static void FreeInputBlob(DATA_BLOB blob)
    {
        if (blob.pbData != IntPtr.Zero)
            Marshal.FreeHGlobal(blob.pbData);
    }

    // Output blobs are allocated by Windows (LocalAlloc) and must be released with LocalFree.
    private static void FreeOutputBlob(DATA_BLOB blob)
    {
        if (blob.pbData != IntPtr.Zero)
            LocalFree(blob.pbData);
    }
}
