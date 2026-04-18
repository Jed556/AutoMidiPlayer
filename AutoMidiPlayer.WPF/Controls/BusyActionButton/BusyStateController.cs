using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace AutoMidiPlayer.WPF.Controls;

/// <summary>
/// Tracks busy state for UI elements and keeps the visible busy indicator on-screen
/// for at least <see cref="MinimumVisibleDuration"/> to provide user feedback.
/// </summary>
public class BusyStateController : FrameworkElement
{
    private CancellationTokenSource? _pendingHideToken;
    private DateTimeOffset _visibleSinceUtc;

    public static readonly DependencyProperty IsBusyProperty = DependencyProperty.Register(
        nameof(IsBusy),
        typeof(bool),
        typeof(BusyStateController),
        new FrameworkPropertyMetadata(false, OnIsBusyChanged));

    public static readonly DependencyProperty MinimumVisibleDurationProperty = DependencyProperty.Register(
        nameof(MinimumVisibleDuration),
        typeof(TimeSpan),
        typeof(BusyStateController),
        new FrameworkPropertyMetadata(TimeSpan.FromSeconds(1), OnMinimumVisibleDurationChanged));

    private static readonly DependencyPropertyKey IsVisibleBusyPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsVisibleBusy),
        typeof(bool),
        typeof(BusyStateController),
        new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IsVisibleBusyProperty = IsVisibleBusyPropertyKey.DependencyProperty;

    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    public TimeSpan MinimumVisibleDuration
    {
        get => (TimeSpan)GetValue(MinimumVisibleDurationProperty);
        set => SetValue(MinimumVisibleDurationProperty, value);
    }

    public bool IsVisibleBusy
    {
        get => (bool)GetValue(IsVisibleBusyProperty);
        private set => SetValue(IsVisibleBusyPropertyKey, value);
    }

    private static void OnIsBusyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BusyStateController controller)
            _ = controller.UpdateVisibleStateAsync(controller.IsBusy);
    }

    private static void OnMinimumVisibleDurationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BusyStateController { IsBusy: false, IsVisibleBusy: true } controller)
            _ = controller.UpdateVisibleStateAsync(false);
    }

    private async Task UpdateVisibleStateAsync(bool isBusy)
    {
        CancelPendingHide();

        if (isBusy)
        {
            _visibleSinceUtc = DateTimeOffset.UtcNow;
            IsVisibleBusy = true;
            return;
        }

        if (!IsVisibleBusy)
            return;

        var minimumDuration = MinimumVisibleDuration < TimeSpan.Zero
            ? TimeSpan.Zero
            : MinimumVisibleDuration;

        var elapsed = DateTimeOffset.UtcNow - _visibleSinceUtc;
        var remaining = minimumDuration - elapsed;
        if (remaining > TimeSpan.Zero)
        {
            var hideToken = new CancellationTokenSource();
            _pendingHideToken = hideToken;

            try
            {
                await Task.Delay(remaining, hideToken.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        if (IsBusy)
            return;

        IsVisibleBusy = false;
    }

    private void CancelPendingHide()
    {
        if (_pendingHideToken is null)
            return;

        _pendingHideToken.Cancel();
        _pendingHideToken.Dispose();
        _pendingHideToken = null;
    }

    protected override void OnVisualParentChanged(DependencyObject oldParent)
    {
        base.OnVisualParentChanged(oldParent);

        if (VisualParent is null)
            CancelPendingHide();
    }
}
