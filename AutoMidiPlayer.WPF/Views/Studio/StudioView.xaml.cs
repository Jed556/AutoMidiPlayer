using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AutoMidiPlayer.WPF.ViewModels;

namespace AutoMidiPlayer.WPF.Views;

public partial class StudioView : UserControl
{
    private const double SidebarWidth = 328.0;
    private readonly TranslateTransform _dockedSidebarTransform = new();
    private readonly TranslateTransform _overlaySidebarTransform = new();
    private StudioViewModel? ViewModel => DataContext as StudioViewModel;

    public StudioView()
    {
        InitializeComponent();
        DockedSidebar.RenderTransform = _dockedSidebarTransform;
        OverlaySidebarControl.RenderTransform = _overlaySidebarTransform;
        TracksDrawerButton.SizeChanged += OnDrawerButtonSizeChanged;
        SettingsDrawerButton.SizeChanged += OnDrawerButtonSizeChanged;
        DataContextChanged += OnDataContextChanged;
        Loaded += OnViewLoaded;
    }

    private void OnDrawerButtonSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width > 0)
        {
            UpdateDrawerSwitcherIndicator(animate: false);
        }
    }

    private void OnViewLoaded(object sender, RoutedEventArgs e)
    {
        UpdateResponsiveLayout(animate: false);
        UpdateDrawerSwitcherIndicator(animate: false);
        InteractiveRoll?.FitKeyHeightToViewport();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged oldVm)
        {
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (e.OldValue is StudioViewModel oldStudioVm)
        {
            oldStudioVm.SongDataReloaded -= OnSongDataReloaded;
        }

        if (e.NewValue is INotifyPropertyChanged newVm)
        {
            newVm.PropertyChanged += OnViewModelPropertyChanged;
        }

        if (e.NewValue is StudioViewModel newStudioVm)
        {
            newStudioVm.SongDataReloaded += OnSongDataReloaded;
        }

        UpdateResponsiveLayout(animate: false);
        UpdateDrawerSwitcherIndicator(animate: false);
    }

    private void OnSongDataReloaded(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess())
        {
            InteractiveRoll?.RefreshNotesAndTimeline();
            InteractiveRoll?.FitKeyHeightToViewport();
        }
        else
        {
            Dispatcher.BeginInvoke(new Action(() => OnSongDataReloaded(sender, e)));
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StudioViewModel.IsSidebarOpen))
        {
            UpdateResponsiveLayout(animate: true);
            UpdateDrawerSwitcherIndicator(animate: true);
        }
        else if (e.PropertyName is nameof(StudioViewModel.ActiveSidebarTab)
            or nameof(StudioViewModel.IsTracksDrawerActive)
            or nameof(StudioViewModel.IsSettingsDrawerActive))
        {
            UpdateDrawerSwitcherIndicator(animate: true);
        }
    }

    private void OnViewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveLayout(animate: false);
        UpdateDrawerSwitcherIndicator(animate: false);
    }

    private void UpdateResponsiveLayout(bool animate = false)
    {
        var vm = ViewModel;
        var isOpen = vm?.IsSidebarOpen ?? true;
        var isWide = ActualWidth >= 1050;

        if (isWide)
        {
            // Reset and hide overlay drawer
            OverlayDrawer.Visibility = Visibility.Collapsed;
            _overlaySidebarTransform.BeginAnimation(TranslateTransform.XProperty, null);
            _overlaySidebarTransform.X = SidebarWidth;

            double targetWidth = isOpen ? SidebarWidth : 0.0;
            double targetX = isOpen ? 0.0 : SidebarWidth;

            if (!animate || !IsLoaded)
            {
                DockedSidebarHost.BeginAnimation(FrameworkElement.WidthProperty, null);
                _dockedSidebarTransform.BeginAnimation(TranslateTransform.XProperty, null);
                DockedSidebarHost.Width = targetWidth;
                _dockedSidebarTransform.X = targetX;
                DockedSidebarHost.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
                return;
            }

            // Animate docked sidebar slide + ease resize
            DockedSidebarHost.Visibility = Visibility.Visible;

            double currentWidth = DockedSidebarHost.ActualWidth;
            if (double.IsNaN(currentWidth) || currentWidth < 0)
            {
                currentWidth = DockedSidebarHost.Width > 0 ? DockedSidebarHost.Width : (isOpen ? 0.0 : SidebarWidth);
            }
            double currentX = _dockedSidebarTransform.X;

            DockedSidebarHost.BeginAnimation(FrameworkElement.WidthProperty, null);
            DockedSidebarHost.Width = currentWidth;
            _dockedSidebarTransform.BeginAnimation(TranslateTransform.XProperty, null);
            _dockedSidebarTransform.X = currentX;

            var ease = new CubicEase
            {
                EasingMode = isOpen ? EasingMode.EaseOut : EasingMode.EaseInOut
            };
            var duration = TimeSpan.FromMilliseconds(isOpen ? 280 : 240);

            var widthAnim = new DoubleAnimation
            {
                From = currentWidth,
                To = targetWidth,
                Duration = duration,
                EasingFunction = ease,
                FillBehavior = FillBehavior.Stop
            };

            widthAnim.Completed += (s, e) =>
            {
                DockedSidebarHost.BeginAnimation(FrameworkElement.WidthProperty, null);
                DockedSidebarHost.Width = targetWidth;
                if (!isOpen)
                {
                    DockedSidebarHost.Visibility = Visibility.Collapsed;
                }
            };

            var transformAnim = new DoubleAnimation
            {
                From = currentX,
                To = targetX,
                Duration = duration,
                EasingFunction = ease,
                FillBehavior = FillBehavior.Stop
            };

            transformAnim.Completed += (s, e) =>
            {
                _dockedSidebarTransform.BeginAnimation(TranslateTransform.XProperty, null);
                _dockedSidebarTransform.X = targetX;
            };

            DockedSidebarHost.BeginAnimation(FrameworkElement.WidthProperty, widthAnim);
            _dockedSidebarTransform.BeginAnimation(TranslateTransform.XProperty, transformAnim);
        }
        else
        {
            // Reset and hide docked sidebar
            DockedSidebarHost.Visibility = Visibility.Collapsed;
            DockedSidebarHost.BeginAnimation(FrameworkElement.WidthProperty, null);
            DockedSidebarHost.Width = 0.0;
            _dockedSidebarTransform.BeginAnimation(TranslateTransform.XProperty, null);
            _dockedSidebarTransform.X = SidebarWidth;

            double targetX = isOpen ? 0.0 : SidebarWidth;

            if (!animate || !IsLoaded)
            {
                _overlaySidebarTransform.BeginAnimation(TranslateTransform.XProperty, null);
                _overlaySidebarTransform.X = targetX;
                OverlayDrawer.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
                return;
            }

            // Animate overlay drawer slide
            OverlayDrawer.Visibility = Visibility.Visible;
            double currentX = _overlaySidebarTransform.X;

            _overlaySidebarTransform.BeginAnimation(TranslateTransform.XProperty, null);
            _overlaySidebarTransform.X = currentX;

            var ease = new CubicEase
            {
                EasingMode = isOpen ? EasingMode.EaseOut : EasingMode.EaseIn
            };
            var duration = TimeSpan.FromMilliseconds(isOpen ? 260 : 200);

            var overlayAnim = new DoubleAnimation
            {
                From = currentX,
                To = targetX,
                Duration = duration,
                EasingFunction = ease,
                FillBehavior = FillBehavior.Stop
            };

            overlayAnim.Completed += (s, e) =>
            {
                _overlaySidebarTransform.BeginAnimation(TranslateTransform.XProperty, null);
                _overlaySidebarTransform.X = targetX;
                if (!isOpen)
                {
                    OverlayDrawer.Visibility = Visibility.Collapsed;
                }
            };

            _overlaySidebarTransform.BeginAnimation(TranslateTransform.XProperty, overlayAnim);
        }
    }

    private void OnPianoRollScrubRequested(object? sender, TimeSpan time)
    {
        ViewModel?.ScrubTo(time);
    }

    private async void OnInteractiveRollMarkerDeleteRequested(object? sender, AutoMidiPlayer.Data.Entities.KeyChangeMarker marker)
    {
        if (ViewModel != null)
        {
            await ViewModel.DeleteMarkerAsync(marker);
        }
    }

    private async void OnInteractiveRollMarkerEditRequested(object? sender, AutoMidiPlayer.Data.Entities.KeyChangeMarker marker)
    {
        if (ViewModel != null)
        {
            await ViewModel.EditMarkerAsync(marker);
        }
    }

    private void UpdateDrawerSwitcherIndicator(bool animate = true)
    {
        var vm = ViewModel;
        if (vm == null || TracksDrawerButton == null || SettingsDrawerButton == null || DrawerActiveIndicator == null || DrawerSwitcherContainer == null)
            return;

        bool isTracksActive = vm.IsTracksDrawerActive;
        bool isSettingsActive = vm.IsSettingsDrawerActive;
        bool isActive = isTracksActive || isSettingsActive;

        if (!isActive)
        {
            if (!animate || !IsLoaded)
            {
                DrawerActiveIndicator.BeginAnimation(UIElement.OpacityProperty, null);
                DrawerActiveIndicator.Opacity = 0.0;
            }
            else
            {
                var fadeOut = new DoubleAnimation
                {
                    To = 0.0,
                    Duration = TimeSpan.FromMilliseconds(180),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                DrawerActiveIndicator.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }
            return;
        }

        FrameworkElement targetButton = isSettingsActive ? SettingsDrawerButton : TracksDrawerButton;

        if (!targetButton.IsLoaded || targetButton.ActualWidth <= 0 || !targetButton.IsDescendantOf(DrawerSwitcherContainer))
        {
            return;
        }

        Point relativePos;
        try
        {
            relativePos = targetButton.TranslatePoint(new Point(0, 0), DrawerSwitcherContainer);
        }
        catch
        {
            return;
        }

        double targetX = relativePos.X;
        double targetWidth = targetButton.ActualWidth;

        if (!animate || !IsLoaded || DrawerActiveIndicator.Opacity < 0.05)
        {
            if (Math.Abs(DrawerActiveIndicator.Width - targetWidth) < 0.5 &&
                Math.Abs(DrawerActiveIndicatorTransform.X - targetX) < 0.5 &&
                Math.Abs(DrawerActiveIndicator.Opacity - 1.0) < 0.05)
            {
                return;
            }

            DrawerActiveIndicatorTransform.BeginAnimation(TranslateTransform.XProperty, null);
            DrawerActiveIndicator.BeginAnimation(FrameworkElement.WidthProperty, null);
            DrawerActiveIndicator.BeginAnimation(UIElement.OpacityProperty, null);

            DrawerActiveIndicatorTransform.X = targetX;
            DrawerActiveIndicator.Width = targetWidth;

            if (DrawerActiveIndicator.Opacity < 0.95)
            {
                if (animate && IsLoaded)
                {
                    var fadeIn = new DoubleAnimation
                    {
                        From = 0.0,
                        To = 1.0,
                        Duration = TimeSpan.FromMilliseconds(180),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    DrawerActiveIndicator.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                }
                else
                {
                    DrawerActiveIndicator.Opacity = 1.0;
                }
            }
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(240);

        var moveAnim = new DoubleAnimation
        {
            To = targetX,
            Duration = duration,
            EasingFunction = ease
        };

        var widthAnim = new DoubleAnimation
        {
            To = targetWidth,
            Duration = duration,
            EasingFunction = ease
        };

        DrawerActiveIndicatorTransform.BeginAnimation(TranslateTransform.XProperty, moveAnim);
        DrawerActiveIndicator.BeginAnimation(FrameworkElement.WidthProperty, widthAnim);

        if (DrawerActiveIndicator.Opacity < 0.95)
        {
            var fadeIn = new DoubleAnimation
            {
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = ease
            };
            DrawerActiveIndicator.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }
    }
}
