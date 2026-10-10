using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AutoMidiPlayer.WPF.ViewModels;

namespace AutoMidiPlayer.WPF.Views;

public partial class StudioSidebarControl : UserControl
{
    private string _currentTab = "Tracks";
    private StudioViewModel? ViewModel => DataContext as StudioViewModel;

    public StudioSidebarControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateTabState(animate: false);
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

        UpdateTabState(animate: false);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StudioViewModel.ActiveSidebarTab))
        {
            var vm = ViewModel;
            bool isDrawerOpen = vm?.IsSidebarOpen ?? true;
            UpdateTabState(animate: IsLoaded && isDrawerOpen);
        }
        else if (e.PropertyName == nameof(StudioViewModel.IsSidebarOpen))
        {
            var vm = ViewModel;
            if (vm != null && !vm.IsSidebarOpen)
            {
                UpdateTabState(animate: false);
            }
        }
    }

    private void UpdateTabState(bool animate)
    {
        var vm = ViewModel;
        string targetTab = vm?.ActiveSidebarTab ?? "Tracks";

        if (!animate || !IsLoaded)
        {
            _currentTab = targetTab;
            SnapToTab(targetTab);
            return;
        }

        if (string.Equals(_currentTab, targetTab, StringComparison.Ordinal))
        {
            return;
        }

        string previousTab = _currentTab;
        _currentTab = targetTab;

        AnimateTransition(previousTab, targetTab);
    }

    private void SnapToTab(string tab)
    {
        TracksContentTransform.BeginAnimation(TranslateTransform.XProperty, null);
        SettingsContentTransform.BeginAnimation(TranslateTransform.XProperty, null);
        TracksHeaderTransform.BeginAnimation(TranslateTransform.XProperty, null);
        SettingsHeaderTransform.BeginAnimation(TranslateTransform.XProperty, null);
        TracksHeaderPanel.BeginAnimation(UIElement.OpacityProperty, null);
        SettingsHeaderPanel.BeginAnimation(UIElement.OpacityProperty, null);

        double width = ActualWidth > 0 ? ActualWidth : (Width > 0 ? Width : 320.0);
        const double headerOffset = 30.0;

        if (tab == "Tracks")
        {
            TracksContentTransform.X = 0;
            TracksContentGrid.Visibility = Visibility.Visible;

            SettingsContentTransform.X = width;
            SettingsContentGrid.Visibility = Visibility.Collapsed;

            TracksHeaderTransform.X = 0;
            TracksHeaderPanel.Opacity = 1;
            TracksHeaderPanel.Visibility = Visibility.Visible;

            SettingsHeaderTransform.X = headerOffset;
            SettingsHeaderPanel.Opacity = 0;
            SettingsHeaderPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            SettingsContentTransform.X = 0;
            SettingsContentGrid.Visibility = Visibility.Visible;

            TracksContentTransform.X = -width;
            TracksContentGrid.Visibility = Visibility.Collapsed;

            SettingsHeaderTransform.X = 0;
            SettingsHeaderPanel.Opacity = 1;
            SettingsHeaderPanel.Visibility = Visibility.Visible;

            TracksHeaderTransform.X = -headerOffset;
            TracksHeaderPanel.Opacity = 0;
            TracksHeaderPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void AnimateTransition(string fromTab, string toTab)
    {
        double width = ActualWidth > 0 ? ActualWidth : (Width > 0 ? Width : 320.0);
        const double headerOffset = 30.0;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(240);

        bool movingToSettings = toTab == "Settings";

        TracksContentTransform.BeginAnimation(TranslateTransform.XProperty, null);
        SettingsContentTransform.BeginAnimation(TranslateTransform.XProperty, null);
        TracksHeaderTransform.BeginAnimation(TranslateTransform.XProperty, null);
        SettingsHeaderTransform.BeginAnimation(TranslateTransform.XProperty, null);
        TracksHeaderPanel.BeginAnimation(UIElement.OpacityProperty, null);
        SettingsHeaderPanel.BeginAnimation(UIElement.OpacityProperty, null);

        if (movingToSettings)
        {
            TracksContentGrid.Visibility = Visibility.Visible;
            SettingsContentGrid.Visibility = Visibility.Visible;
            SettingsContentTransform.X = width;

            TracksHeaderPanel.Visibility = Visibility.Visible;
            SettingsHeaderPanel.Visibility = Visibility.Visible;
            SettingsHeaderTransform.X = headerOffset;
            SettingsHeaderPanel.Opacity = 0;

            var tracksAnim = new DoubleAnimation { To = -width, Duration = duration, EasingFunction = ease };
            var settingsAnim = new DoubleAnimation { To = 0, Duration = duration, EasingFunction = ease };

            var tracksHeaderAnim = new DoubleAnimation { To = -headerOffset, Duration = duration, EasingFunction = ease };
            var tracksHeaderFade = new DoubleAnimation { To = 0, Duration = duration, EasingFunction = ease };
            var settingsHeaderAnim = new DoubleAnimation { To = 0, Duration = duration, EasingFunction = ease };
            var settingsHeaderFade = new DoubleAnimation { To = 1, Duration = duration, EasingFunction = ease };

            settingsAnim.Completed += (s, e) =>
            {
                if (_currentTab == "Settings")
                {
                    TracksContentGrid.Visibility = Visibility.Collapsed;
                    TracksHeaderPanel.Visibility = Visibility.Collapsed;
                }
            };

            TracksContentTransform.BeginAnimation(TranslateTransform.XProperty, tracksAnim);
            SettingsContentTransform.BeginAnimation(TranslateTransform.XProperty, settingsAnim);

            TracksHeaderTransform.BeginAnimation(TranslateTransform.XProperty, tracksHeaderAnim);
            TracksHeaderPanel.BeginAnimation(UIElement.OpacityProperty, tracksHeaderFade);
            SettingsHeaderTransform.BeginAnimation(TranslateTransform.XProperty, settingsHeaderAnim);
            SettingsHeaderPanel.BeginAnimation(UIElement.OpacityProperty, settingsHeaderFade);
        }
        else
        {
            TracksContentGrid.Visibility = Visibility.Visible;
            SettingsContentGrid.Visibility = Visibility.Visible;
            TracksContentTransform.X = -width;

            TracksHeaderPanel.Visibility = Visibility.Visible;
            SettingsHeaderPanel.Visibility = Visibility.Visible;
            TracksHeaderTransform.X = -headerOffset;
            TracksHeaderPanel.Opacity = 0;

            var settingsAnim = new DoubleAnimation { To = width, Duration = duration, EasingFunction = ease };
            var tracksAnim = new DoubleAnimation { To = 0, Duration = duration, EasingFunction = ease };

            var settingsHeaderAnim = new DoubleAnimation { To = headerOffset, Duration = duration, EasingFunction = ease };
            var settingsHeaderFade = new DoubleAnimation { To = 0, Duration = duration, EasingFunction = ease };
            var tracksHeaderAnim = new DoubleAnimation { To = 0, Duration = duration, EasingFunction = ease };
            var tracksHeaderFade = new DoubleAnimation { To = 1, Duration = duration, EasingFunction = ease };

            tracksAnim.Completed += (s, e) =>
            {
                if (_currentTab == "Tracks")
                {
                    SettingsContentGrid.Visibility = Visibility.Collapsed;
                    SettingsHeaderPanel.Visibility = Visibility.Collapsed;
                }
            };

            TracksContentTransform.BeginAnimation(TranslateTransform.XProperty, tracksAnim);
            SettingsContentTransform.BeginAnimation(TranslateTransform.XProperty, settingsAnim);

            SettingsHeaderTransform.BeginAnimation(TranslateTransform.XProperty, settingsHeaderAnim);
            SettingsHeaderPanel.BeginAnimation(UIElement.OpacityProperty, settingsHeaderFade);
            TracksHeaderTransform.BeginAnimation(TranslateTransform.XProperty, tracksHeaderAnim);
            TracksHeaderPanel.BeginAnimation(UIElement.OpacityProperty, tracksHeaderFade);
        }
    }
}
