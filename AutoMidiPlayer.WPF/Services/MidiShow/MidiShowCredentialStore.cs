using System;
using System.IO;
using System.Text.Json;
using AutoMidiPlayer.Data;

namespace AutoMidiPlayer.WPF.Services.MidiShow;

/// <summary>
/// A stored MidiShow account credential (per the user of this machine).
/// </summary>
public sealed record MidiShowCredentials(string Username, string Password);

/// <summary>
/// Legacy single-account MidiShow credential store (DPAPI, current user). Superseded by
/// <see cref="MidiShowAccountStore"/>; retained only so an existing login can be loaded and
/// migrated into the new account pool. New code should use <see cref="MidiShowAccountStore"/>.
/// </summary>
public static class MidiShowCredentialStore
{
    public static bool HasCredentials => File.Exists(AppPaths.MidiShowCredentialsPath);

    public static void Save(MidiShowCredentials credentials)
    {
        AppPaths.EnsureDirectoryExists();

        var json = JsonSerializer.SerializeToUtf8Bytes(credentials);
        var protectedBytes = MidiShowProtectedStorage.Protect(json);
        File.WriteAllBytes(AppPaths.MidiShowCredentialsPath, protectedBytes);
    }

    public static MidiShowCredentials? Load()
    {
        try
        {
            if (!File.Exists(AppPaths.MidiShowCredentialsPath))
                return null;

            var protectedBytes = File.ReadAllBytes(AppPaths.MidiShowCredentialsPath);
            var json = MidiShowProtectedStorage.Unprotect(protectedBytes);
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
}
