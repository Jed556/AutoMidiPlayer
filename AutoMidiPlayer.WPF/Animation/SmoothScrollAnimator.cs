using System;
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Media;

namespace AutoMidiPlayer.WPF.Helpers;

public enum SmoothScrollAxis
{
    Vertical,
    Horizontal
}

public readonly struct SmoothScrollAnimatorOptions
{
    public double SmoothingFactor { get; init; }

    public double SnapThreshold { get; init; }

    public double MaxStep { get; init; }

    public double ReferenceFrameRate { get; init; }

    public double MinFrameSeconds { get; init; }

    public double MaxFrameSeconds { get; init; }

    public static SmoothScrollAnimatorOptions Default => new()
    {
        SmoothingFactor = 0.24d,
        SnapThreshold = 2.0d,
        MaxStep = 72d,
        ReferenceFrameRate = 60d,
        MinFrameSeconds = 1d / 240d,
        MaxFrameSeconds = 1d / 24d
    };
}

public sealed class SmoothScrollAnimator : IDisposable
{
    private readonly ScrollViewer _viewer;
    private readonly Action? _onFrameApplied;
    private readonly SmoothScrollAxis _axis;
    private readonly Stopwatch _stopwatch = new();
    private readonly double _smoothingRatePerSecond;
    private readonly double _snapThreshold;
    private readonly double _maxStep;
    private readonly double _referenceFrameRate;
    private readonly double _minFrameSeconds;
    private readonly double _maxFrameSeconds;

    private double _lastFrameSeconds;
    private bool _isRunning;

    public SmoothScrollAnimator(ScrollViewer viewer, SmoothScrollAnimatorOptions options, Action? onFrameApplied = null, SmoothScrollAxis axis = SmoothScrollAxis.Vertical)
    {
        _viewer = viewer;
        _onFrameApplied = onFrameApplied;
        _axis = axis;

        var smoothingFactor = Math.Clamp(options.SmoothingFactor, 0.01d, 0.95d);
        _snapThreshold = Math.Max(0.01d, options.SnapThreshold);
        _maxStep = Math.Max(0.1d, options.MaxStep);
        _referenceFrameRate = Math.Clamp(options.ReferenceFrameRate, 30d, 240d);
        _minFrameSeconds = Math.Clamp(options.MinFrameSeconds, 1d / 1000d, 1d / 30d);
        _maxFrameSeconds = Math.Clamp(options.MaxFrameSeconds, _minFrameSeconds, 1d / 5d);

        _smoothingRatePerSecond = -Math.Log(1d - smoothingFactor) * _referenceFrameRate;
        TargetOffset = GetCurrentOffset();
    }

    public bool IsRunning => _isRunning;

    public double TargetOffset { get; private set; }

    public void SyncTargetToCurrentOffset()
    {
        TargetOffset = GetCurrentOffset();
    }

    public void SetTargetOffset(double targetOffset, bool startIfNeeded = true, bool immediateStep = false)
    {
        var maxOffset = GetScrollableExtent();
        TargetOffset = Math.Clamp(targetOffset, 0d, maxOffset);

        if (!startIfNeeded)
            return;

        EnsureRunning();
        if (immediateStep)
            AdvanceFrame(1d / _referenceFrameRate);
    }

    public void ApplyDelta(double deltaOffset, double minTarget, double maxTarget, bool resetOnDirectionChange)
    {
        if (GetScrollableExtent() <= 0)
            return;

        if (!_isRunning)
            TargetOffset = GetCurrentOffset();

        var currentOffset = GetCurrentOffset();
        if (resetOnDirectionChange)
        {
            var currentDirection = Math.Sign(TargetOffset - currentOffset);
            var incomingDirection = Math.Sign(deltaOffset);
            if (_isRunning && currentDirection != 0 && incomingDirection != 0 && currentDirection != incomingDirection)
                TargetOffset = currentOffset;
        }

        TargetOffset = Math.Clamp(TargetOffset + deltaOffset, minTarget, maxTarget);

        if (!_isRunning)
        {
            EnsureRunning();
            AdvanceFrame(1d / _referenceFrameRate);
        }
    }

    public void Stop(bool snapToTarget = false)
    {
        if (_isRunning)
        {
            CompositionTarget.Rendering -= OnRendering;
            _isRunning = false;
        }

        var scrollableExtent = GetScrollableExtent();
        if (snapToTarget && scrollableExtent > 0)
        {
            ScrollToOffset(Math.Clamp(TargetOffset, 0d, scrollableExtent));
            _onFrameApplied?.Invoke();
        }

        _stopwatch.Reset();
        _lastFrameSeconds = 0d;
    }

    public void Dispose()
    {
        Stop();
    }

    private void EnsureRunning()
    {
        if (_isRunning)
            return;

        _isRunning = true;
        _stopwatch.Restart();
        _lastFrameSeconds = 0d;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!_isRunning)
            return;

        if (GetScrollableExtent() <= 0)
        {
            TargetOffset = 0d;
            Stop();
            _onFrameApplied?.Invoke();
            return;
        }

        var nowSeconds = _stopwatch.Elapsed.TotalSeconds;
        var deltaSeconds = nowSeconds - _lastFrameSeconds;
        if (deltaSeconds <= 0d)
            return;

        _lastFrameSeconds = nowSeconds;
        deltaSeconds = Math.Clamp(deltaSeconds, _minFrameSeconds, _maxFrameSeconds);

        AdvanceFrame(deltaSeconds);
    }

    private void AdvanceFrame(double deltaSeconds)
    {
        var scrollableExtent = GetScrollableExtent();
        TargetOffset = Math.Clamp(TargetOffset, 0d, scrollableExtent);

        var currentOffset = GetCurrentOffset();
        var delta = TargetOffset - currentOffset;

        if (Math.Abs(delta) <= _snapThreshold)
        {
            Stop();
            ScrollToOffset(TargetOffset);
            _onFrameApplied?.Invoke();
            return;
        }

        var frameBlend = 1d - Math.Exp(-_smoothingRatePerSecond * deltaSeconds);
        frameBlend = Math.Clamp(frameBlend, 0d, 1d);

        var maxStepForFrame = _maxStep * deltaSeconds * _referenceFrameRate;
        var smoothStep = Math.Clamp(delta * frameBlend, -maxStepForFrame, maxStepForFrame);
        var nextOffset = Math.Clamp(currentOffset + smoothStep, 0d, scrollableExtent);

        ScrollToOffset(nextOffset);
        _onFrameApplied?.Invoke();
    }

    private double GetCurrentOffset()
    {
        return _axis == SmoothScrollAxis.Horizontal ? _viewer.HorizontalOffset : _viewer.VerticalOffset;
    }

    private double GetScrollableExtent()
    {
        return _axis == SmoothScrollAxis.Horizontal ? _viewer.ScrollableWidth : _viewer.ScrollableHeight;
    }

    private void ScrollToOffset(double offset)
    {
        if (_axis == SmoothScrollAxis.Horizontal)
            _viewer.ScrollToHorizontalOffset(offset);
        else
            _viewer.ScrollToVerticalOffset(offset);
    }
}
