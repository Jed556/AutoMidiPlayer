using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using AutoMidiPlayer.Data;

namespace AutoMidiPlayer.WPF.Services.MidiShow;

/// <summary>
/// A stored MidiShow account credential (per the user of this machine).
/// </summary>
public sealed record MidiShowCredentials(string Username, string Password);

/// <summary>
/// Persists the user's own MidiShow login credentials, encrypted with Windows DPAPI
/// scoped to the current user. The encrypted blob can only be decrypted by the same
/// Windows account on the same machine, so credentials never travel with the app and
/// are not recoverable from a copied file.
/// </summary>
public static class MidiShowCredentialStore
{
    // Extra entropy mixed into DPAPI so other apps can't trivially decrypt the blob.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AutoMidiPlayer.MidiShow.v1");

    public static bool HasCredentials => File.Exists(AppPaths.MidiShowCredentialsPath);

    public static void Save(MidiShowCredentials credentials)
    {
        AppPaths.EnsureDirectoryExists();

        var json = JsonSerializer.SerializeToUtf8Bytes(credentials);
        var protectedBytes = Protect(json);
        File.WriteAllBytes(AppPaths.MidiShowCredentialsPath, protectedBytes);
    }

    public static MidiShowCredentials? Load()
    {
        try
        {
            if (!File.Exists(AppPaths.MidiShowCredentialsPath))
                return null;

            var protectedBytes = File.ReadAllBytes(AppPaths.MidiShowCredentialsPath);
            var json = Unprotect(protectedBytes);
            if (json is null)
                return null;

            var credentials = JsonSerializer.Deserialize<MidiShowCredentials>(json);
            if (credentials is null || string.IsNullOrWhiteSpace(credentials.Username))
                return null;

            return credentials;
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to load MidiShow credentials.");
            Logger.LogException(ex);
            return null;
        }
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(AppPaths.MidiShowCredentialsPath))
                File.Delete(AppPaths.MidiShowCredentialsPath);
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to clear MidiShow credentials.");
            Logger.LogException(ex);
        }
    }

    #region DPAPI (CurrentUser)

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

    private static byte[] Protect(byte[] data)
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

    private static byte[]? Unprotect(byte[] data)
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

    #endregion
}
