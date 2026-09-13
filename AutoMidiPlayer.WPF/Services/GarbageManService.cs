using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace AutoMidiPlayer.WPF.Services;

/// <summary>
/// The GarbageMan is responsible for taking out the trash.
/// 
/// "Why does this work? Nobody knows. Only God and the GarbageMan know."
/// 
/// In all seriousness, WPF's unmanaged memory usage (specifically when rapidly decoding
/// remote BitmapImages and networking buffers) can sometimes outpace the .NET Garbage Collector's 
/// heuristic triggers on lower-end devices. This service steps in to forcefully take out the trash.
/// </summary>
public static class GarbageManService
{
    private static DateTime _lastCollection = DateTime.MinValue;
    private static readonly TimeSpan _minInterval = TimeSpan.FromSeconds(2);
    private static int _isTrailingSweepScheduled = 0;

    /// <summary>
    /// Optional predicate to check if playback is currently active.
    /// If true, garbage collection and working set trimming are skipped to prevent playback stutter.
    /// </summary>
    public static Func<bool>? IsPlaybackActive { get; set; }

    /// <summary>
    /// Performs a full Gen 2 garbage collection with finalizer drain.
    /// Use sparingly, as this halts the execution engine.
    /// </summary>
    public static void TakeOutTheTrash(bool aggressive = false)
    {
        if (IsPlaybackActive?.Invoke() == true)
            return;

        // If we are spammed with requests, always schedule a trailing sweep to run a few seconds 
        // later. This guarantees that unmanaged thumbnail downloads that finish asynchronously 
        // AFTER the page loads will still be cleaned up when the user stops clicking "Next".
        if (Interlocked.Exchange(ref _isTrailingSweepScheduled, 1) == 0)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(_minInterval);
                _isTrailingSweepScheduled = 0;
                if (IsPlaybackActive?.Invoke() == true)
                    return;
                DoSweep();
            });
        }

        var now = DateTime.UtcNow;
        if (!aggressive && now - _lastCollection < _minInterval)
            return; // Don't overwork the GarbageMan

        _lastCollection = now;
        _ = Task.Run(DoSweep);
    }

    [System.Runtime.InteropServices.DllImport("psapi.dll")]
    private static extern int EmptyWorkingSet(IntPtr hwProc);

    private static void DoSweep()
    {
        if (IsPlaybackActive?.Invoke() == true)
            return;

        // "I don't know why we have to tell the computer to clean up after itself. 
        // It's 2026, you'd think it would know better." - The GarbageMan
        
        try 
        {
            // Compact the Large Object Heap (where big network strings and arrays get stuck)
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = 
                System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            
            // Sweep the generations. Must be blocking: true for LOH compaction to actually occur!
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            
            // Wait for finalizers (this is where WPF's unmanaged BitmapImages actually get freed)
            GC.WaitForPendingFinalizers();

            // One more sweep to collect the finalized objects
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);

            // Force the OS to page out unused CLR segments so Task Manager shows the real memory footprint
            var process = System.Diagnostics.Process.GetCurrentProcess();
            EmptyWorkingSet(process.Handle);

#if DEBUG
            LogMemory("After GC");
#endif
        }
        catch
        {
            // The GarbageMan never complains, he just keeps working.
        }
    }

#if DEBUG
    /// <summary>
    /// Diagnostic helper: logs Working Set, Private Bytes, and GC heap metrics to the Debug output window.
    /// Only compiled in Debug builds to avoid computation overhead in production.
    /// </summary>
    private static void LogMemory(string label)
    {
        using var process = Process.GetCurrentProcess();
        var gcInfo = GC.GetGCMemoryInfo();
        Debug.WriteLine(
            $"[GarbageMan] {label,-25} | " +
            $"Working Set: {process.WorkingSet64 / 1024d / 1024d,7:F1} MB | " +
            $"Private Bytes: {process.PrivateMemorySize64 / 1024d / 1024d,7:F1} MB | " +
            $"Managed Allocated: {GC.GetTotalMemory(false) / 1024d / 1024d,7:F1} MB | " +
            $"Heap Size: {gcInfo.HeapSizeBytes / 1024d / 1024d,7:F1} MB | " +
            $"Fragmented: {gcInfo.FragmentedBytes / 1024d / 1024d,7:F1} MB");
    }
#endif
}
