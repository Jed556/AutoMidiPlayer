using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutoMidiPlayer.Data.Properties;

namespace AutoMidiPlayer.WPF.Services;

/// <summary>
/// Stores MIDI paths that should not be auto-imported from the watched MIDI folder.
/// </summary>
public static class AutoImportExclusionStore
{
    private static readonly Settings Settings = Settings.Default;

    public static bool IsMidiFilePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".mid", StringComparison.OrdinalIgnoreCase)
               || string.Equals(extension, ".midi", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPathWithinFolder(string? path, string? folderPath)
    {
        var normalizedPath = NormalizePath(path);
        var normalizedFolder = NormalizePath(folderPath);

        if (string.IsNullOrWhiteSpace(normalizedPath) || string.IsNullOrWhiteSpace(normalizedFolder))
            return false;

        var folderWithSeparator = normalizedFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                 + Path.DirectorySeparatorChar;

        return normalizedPath.StartsWith(folderWithSeparator, StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalizedPath, normalizedFolder, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsExcluded(string? path)
    {
        var normalizedPath = NormalizePath(path);
        if (string.IsNullOrWhiteSpace(normalizedPath))
            return false;

        var excludedPaths = ReadSet();
        return excludedPaths.Contains(normalizedPath);
    }

    public static void Add(string? path)
    {
        var normalizedPath = NormalizePath(path);
        if (string.IsNullOrWhiteSpace(normalizedPath))
            return;

        var excludedPaths = ReadSet();
        if (excludedPaths.Add(normalizedPath))
            SaveSet(excludedPaths);
    }

    public static void Remove(string? path)
    {
        var normalizedPath = NormalizePath(path);
        if (string.IsNullOrWhiteSpace(normalizedPath))
            return;

        var excludedPaths = ReadSet();
        if (excludedPaths.Remove(normalizedPath))
            SaveSet(excludedPaths);
    }

    public static IReadOnlyList<string> GetExcludedPaths()
    {
        return ReadSet()
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<string> GetExistingExcludedMidiFiles(string? folderPath = null)
    {
        return ReadSet()
            .Where(File.Exists)
            .Where(IsMidiFilePath)
            .Where(path => string.IsNullOrWhiteSpace(folderPath) || IsPathWithinFolder(path, folderPath))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static void PruneMissingPaths()
    {
        var excludedPaths = ReadSet();
        var changed = false;

        foreach (var path in excludedPaths.ToList())
        {
            if (!File.Exists(path))
            {
                excludedPaths.Remove(path);
                changed = true;
            }
        }

        if (changed)
            SaveSet(excludedPaths);
    }

    private static HashSet<string> ReadSet()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var serialized = Settings.ExcludedAutoImportPaths;

        if (string.IsNullOrWhiteSpace(serialized))
            return result;

        var paths = serialized.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var path in paths)
        {
            var normalizedPath = NormalizePath(path);
            if (!string.IsNullOrWhiteSpace(normalizedPath))
                result.Add(normalizedPath);
        }

        return result;
    }

    private static void SaveSet(HashSet<string> excludedPaths)
    {
        var serialized = string.Join(Environment.NewLine,
            excludedPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase));

        Settings.Modify(settings => settings.ExcludedAutoImportPaths = serialized);
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var trimmed = path.Trim();

        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch
        {
            return trimmed;
        }
    }
}
