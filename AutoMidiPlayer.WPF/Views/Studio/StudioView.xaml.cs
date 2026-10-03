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
        DataContextChanged += OnDataContextChanged;
        Loaded += OnViewLoaded;
    }

    private void OnViewLoaded(object sender, RoutedEventArgs e)
    {
        UpdateResponsiveLayout(animate: false);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged oldVm)
        {
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (e.NewValue is INotifyPropertyChanged newVm)
        {
            newVm.PropertyChanged += OnViewModelPropertyChanged;
        }

        UpdateResponsiveLayout(animate: false);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StudioViewModel.IsSidebarOpen))
        {
            UpdateResponsiveLayout(animate: true);
        }
    }

    private void OnViewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveLayout(animate: false);
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
}
