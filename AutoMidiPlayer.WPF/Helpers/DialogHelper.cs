using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AutoMidiPlayer.Data.Properties;
using AutoMidiPlayer.WPF.Animation.Transitions;
using AutoMidiPlayer.WPF.Dialogs;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Helpers;

public enum DialogActionOutcome
{
    Cancelled,
    Confirmed,
    Custom
}

public sealed class DialogActionButton
{
    public string Text { get; init; } = string.Empty;

    public ControlAppearance Appearance { get; init; } = ControlAppearance.Secondary;

    public Func<Task>? CallbackAsync { get; init; }
}

public sealed class DialogActionRequest
{
    public string Title { get; init; } = string.Empty;

    public SymbolRegular? Icon { get; init; }

    public string? Body { get; init; }

    public object? Content { get; init; }

    public DialogActionButton? ConfirmButton { get; init; } = new()
    {
        Text = "Confirm",
        Appearance = ControlAppearance.Primary
    };

    public DialogActionButton? CancelButton { get; init; } = new()
    {
        Text = "Cancel",
        Appearance = ControlAppearance.Secondary
    };

    public DialogActionButton? CustomButton { get; init; }
}

public sealed class DialogHostSetupOptions
{
    public bool EnableScrollAutoFade { get; init; } = true;
}

/// <summary>
/// Helper class for creating ContentDialogs with proper DialogHost setup.
/// </summary>
public static class DialogHelper
{
    private static readonly DependencyProperty IsOpenAnimationHookedProperty =
        DependencyProperty.RegisterAttached(
            "IsOpenAnimationHooked",
            typeof(bool),
            typeof(DialogHelper),
            new PropertyMetadata(false));

    private static readonly DependencyProperty IsPreOpenStateHookedProperty =
        DependencyProperty.RegisterAttached(
            "IsPreOpenStateHooked",
            typeof(bool),
            typeof(DialogHelper),
            new PropertyMetadata(false));

    private static readonly DependencyProperty IsScrollBehaviorHookedProperty =
        DependencyProperty.RegisterAttached(
            "IsScrollBehaviorHooked",
            typeof(bool),
            typeof(DialogHelper),
            new PropertyMetadata(false));

    private static readonly DependencyProperty EnableScrollAutoFadeProperty =
        DependencyProperty.RegisterAttached(
            "EnableScrollAutoFade",
            typeof(bool),
            typeof(DialogHelper),
            new PropertyMetadata(true));

    private enum DialogEntranceAnimationMode
    {
        None,
        Entrance,
        DrillIn,
        SlideFromLeft,
        SlideFromRight,
        SlideFromBottom
    }

    private static bool GetIsOpenAnimationHooked(DependencyObject obj) => (bool)obj.GetValue(IsOpenAnimationHookedProperty);

    private static void SetIsOpenAnimationHooked(DependencyObject obj, bool value) => obj.SetValue(IsOpenAnimationHookedProperty, value);

    private static bool GetIsPreOpenStateHooked(DependencyObject obj) => (bool)obj.GetValue(IsPreOpenStateHookedProperty);

    private static void SetIsPreOpenStateHooked(DependencyObject obj, bool value) => obj.SetValue(IsPreOpenStateHookedProperty, value);

    private static bool GetIsScrollBehaviorHooked(DependencyObject obj) => (bool)obj.GetValue(IsScrollBehaviorHookedProperty);

    private static void SetIsScrollBehaviorHooked(DependencyObject obj, bool value) => obj.SetValue(IsScrollBehaviorHookedProperty, value);

    private static bool GetEnableScrollAutoFade(DependencyObject obj) => (bool)obj.GetValue(EnableScrollAutoFadeProperty);

    private static void SetEnableScrollAutoFade(DependencyObject obj, bool value) => obj.SetValue(EnableScrollAutoFadeProperty, value);

    /// <summary>
    /// Creates a new ContentDialog with the DialogHostEx property already set.
    /// </summary>
    public static ContentDialog CreateDialog(DialogHostSetupOptions? setupOptions = null)
    {
        var dialog = new ContentDialog();
        SetupDialogHost(dialog, setupOptions);

        if (Application.Current.TryFindResource(typeof(ContentDialog)) is Style dialogStyle)
            dialog.Style = dialogStyle;

        return dialog;
    }

    /// <summary>
    /// Sets up the DialogHostEx property for an existing ContentDialog.
    /// </summary>
    public static bool SetupDialogHost(ContentDialog dialog, DialogHostSetupOptions? setupOptions = null)
    {
        if (setupOptions is not null)
            SetEnableScrollAutoFade(dialog, setupOptions.EnableScrollAutoFade);

        AttachDialogScrollBehavior(dialog);

        if (dialog.DialogHostEx != null)
        {
            AttachOpenAnimation(dialog);
            return true;
        }

        var app = Application.Current;
        if (app == null)
            return false;

        var windows = app.Windows.OfType<Window>().ToList();
        var activeWindow = windows.FirstOrDefault(w => w.IsActive)
                           ?? app.MainWindow
                           ?? windows.FirstOrDefault(w => w.IsVisible)
                           ?? windows.FirstOrDefault();

        if (activeWindow == null)
            return false;

        var dialogHost = ContentDialogHost.GetForWindow(activeWindow);

        if (dialogHost == null)
        {
            foreach (var window in windows)
            {
                dialogHost = ContentDialogHost.GetForWindow(window);
                if (dialogHost != null)
                    break;
            }
        }

        if (dialogHost == null)
            return false;

        dialog.DialogHostEx = dialogHost;
        AttachOpenAnimation(dialog);
        return true;
    }

    private static void AttachDialogScrollBehavior(ContentDialog dialog)
    {
        if (GetIsScrollBehaviorHooked(dialog))
        {
            if (dialog.IsLoaded)
                ApplyDialogScrollBehavior(dialog);

            return;
        }

        SetIsScrollBehaviorHooked(dialog, true);
        dialog.Loaded += OnDialogLoaded;

        if (dialog.IsLoaded)
            ApplyDialogScrollBehavior(dialog);
    }

    private static void OnDialogLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ContentDialog dialog)
            return;

        ApplyDialogScrollBehavior(dialog);
    }

    private static void ApplyDialogScrollBehavior(ContentDialog dialog)
    {
        if (!GetEnableScrollAutoFade(dialog))
            return;

        var viewer = ResolveDialogScrollViewer(dialog);
        if (viewer is not null)
        {
            ScrollViewerAutoFadeBehavior.SetIsEnabled(viewer, true);
            return;
        }

        _ = dialog.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() =>
            {
                if (!GetEnableScrollAutoFade(dialog))
                    return;

                var deferredViewer = ResolveDialogScrollViewer(dialog);
                if (deferredViewer is null)
                    return;

                ScrollViewerAutoFadeBehavior.SetIsEnabled(deferredViewer, true);
            }));
    }

    private static ScrollViewer? ResolveDialogScrollViewer(ContentDialog dialog)
    {
        dialog.ApplyTemplate();

        if (dialog.Template?.FindName("PART_ScrollViewer", dialog) is ScrollViewer partScrollViewer)
            return partScrollViewer;

        return FindDescendant<ScrollViewer>(dialog);
    }

    private static void AttachOpenAnimation(ContentDialog dialog)
    {
        if (GetIsOpenAnimationHooked(dialog))
            return;

        SetIsOpenAnimationHooked(dialog, true);

        if (!GetIsPreOpenStateHooked(dialog))
        {
            SetIsPreOpenStateHooked(dialog, true);
            dialog.IsVisibleChanged += OnDialogIsVisibleChanged;
        }

        dialog.Opened += OnDialogOpened;
    }

    private static void OnDialogIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not ContentDialog dialog)
            return;

        if (e.NewValue is not bool isVisible || !isVisible)
            return;

        PrepareDialogForOpenAnimation(dialog);
    }

    private static void OnDialogOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is not ContentDialog dialog)
            return;

        var animationMode = ResolveDialogEntranceAnimationMode();
        if (animationMode != DialogEntranceAnimationMode.None && dialog.Opacity >= 0.99)
            PrepareDialogForOpenAnimation(dialog, animationMode);

        dialog.Dispatcher.BeginInvoke(
            new Action(() =>
            {
                var shouldAnimate = animationMode != DialogEntranceAnimationMode.None;
                AnimateBackdropOpen(dialog, shouldAnimate);

                if (shouldAnimate)
                    AnimateDialogOpen(dialog, animationMode);
                else
                    ResetDialogToRestState(dialog);
            }),
            DispatcherPriority.Loaded);
    }

    private static void PrepareDialogForOpenAnimation(ContentDialog dialog)
    {
        PrepareDialogForOpenAnimation(dialog, ResolveDialogEntranceAnimationMode());
    }

    private static void PrepareDialogForOpenAnimation(ContentDialog dialog, DialogEntranceAnimationMode mode)
    {
        var (scale, translate) = EnsureDialogTransforms(dialog);
        StopDialogAnimations(dialog, scale, translate);

        if (mode == DialogEntranceAnimationMode.None)
        {
            ResetDialogToRestState(dialog, scale, translate);
            return;
        }

        dialog.RenderTransformOrigin = new Point(0.5, 0.5);
        dialog.Opacity = 0;

        switch (mode)
        {
            case DialogEntranceAnimationMode.Entrance:
                scale.ScaleX = 0.965;
                scale.ScaleY = 0.965;
                translate.X = 0;
                translate.Y = 24;
                break;

            case DialogEntranceAnimationMode.DrillIn:
                scale.ScaleX = 0.9;
                scale.ScaleY = 0.9;
                translate.X = 0;
                translate.Y = 0;
                break;

            case DialogEntranceAnimationMode.SlideFromLeft:
                scale.ScaleX = 1;
                scale.ScaleY = 1;
                translate.X = -40;
                translate.Y = 0;
                break;

            case DialogEntranceAnimationMode.SlideFromRight:
                scale.ScaleX = 1;
                scale.ScaleY = 1;
                translate.X = 40;
                translate.Y = 0;
                break;

            case DialogEntranceAnimationMode.SlideFromBottom:
                scale.ScaleX = 1;
                scale.ScaleY = 1;
                translate.X = 0;
                translate.Y = 34;
                break;
        }
    }

    private static void AnimateDialogOpen(ContentDialog dialog, DialogEntranceAnimationMode mode)
    {
        var (scale, translate) = EnsureDialogTransforms(dialog);
        StopDialogAnimations(dialog, scale, translate);

        var duration = GetDialogAnimationDuration(mode);
        var growEase = new CubicEase { EasingMode = EasingMode.EaseOut };
        var moveEase = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fadeEase = new QuadraticEase { EasingMode = EasingMode.EaseOut };

        var fromScaleX = scale.ScaleX;
        var fromScaleY = scale.ScaleY;
        var fromTranslateX = translate.X;
        var fromTranslateY = translate.Y;
        var fromOpacity = dialog.Opacity;

        var scaleXAnimation = new DoubleAnimation(fromScaleX, 1, duration)
        {
            EasingFunction = growEase,
            FillBehavior = FillBehavior.Stop
        };
        scaleXAnimation.Completed += (_, _) => scale.ScaleX = 1;

        var scaleYAnimation = new DoubleAnimation(fromScaleY, 1, duration)
        {
            EasingFunction = growEase,
            FillBehavior = FillBehavior.Stop
        };
        scaleYAnimation.Completed += (_, _) => scale.ScaleY = 1;

        var translateXAnimation = new DoubleAnimation(fromTranslateX, 0, duration)
        {
            EasingFunction = moveEase,
            FillBehavior = FillBehavior.Stop
        };
        translateXAnimation.Completed += (_, _) => translate.X = 0;

        var translateYAnimation = new DoubleAnimation(fromTranslateY, 0, duration)
        {
            EasingFunction = moveEase,
            FillBehavior = FillBehavior.Stop
        };
        translateYAnimation.Completed += (_, _) => translate.Y = 0;

        var opacityAnimation = new DoubleAnimation(fromOpacity, 1, TimeSpan.FromMilliseconds(Math.Max(140, duration.TotalMilliseconds - 20)))
        {
            EasingFunction = fadeEase,
            FillBehavior = FillBehavior.Stop
        };
        opacityAnimation.Completed += (_, _) => dialog.Opacity = 1;

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnimation, HandoffBehavior.SnapshotAndReplace);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnimation, HandoffBehavior.SnapshotAndReplace);
        translate.BeginAnimation(TranslateTransform.XProperty, translateXAnimation, HandoffBehavior.SnapshotAndReplace);
        translate.BeginAnimation(TranslateTransform.YProperty, translateYAnimation, HandoffBehavior.SnapshotAndReplace);
        dialog.BeginAnimation(UIElement.OpacityProperty, opacityAnimation, HandoffBehavior.SnapshotAndReplace);
    }

    private static TimeSpan GetDialogAnimationDuration(DialogEntranceAnimationMode mode)
    {
        return mode switch
        {
            DialogEntranceAnimationMode.DrillIn => TimeSpan.FromMilliseconds(280),
            DialogEntranceAnimationMode.SlideFromBottom => TimeSpan.FromMilliseconds(260),
            DialogEntranceAnimationMode.SlideFromLeft => TimeSpan.FromMilliseconds(250),
            DialogEntranceAnimationMode.SlideFromRight => TimeSpan.FromMilliseconds(250),
            _ => TimeSpan.FromMilliseconds(240)
        };
    }

    private static DialogEntranceAnimationMode ResolveDialogEntranceAnimationMode()
    {
        var transitions = TransitionCollection.Transitions;
        if (transitions.Count == 0)
            return DialogEntranceAnimationMode.Entrance;

        var selectedTransitionIndex = Settings.Default.SelectedTransition;
        if (selectedTransitionIndex < 0 || selectedTransitionIndex >= transitions.Count)
            selectedTransitionIndex = 0;

        var transition = transitions[selectedTransitionIndex].Object;
        return transition switch
        {
            SuppressTransition => DialogEntranceAnimationMode.None,
            DrillInTransition => DialogEntranceAnimationMode.DrillIn,
            SlideTransition { Effect: Direction.FromLeft } => DialogEntranceAnimationMode.SlideFromLeft,
            SlideTransition { Effect: Direction.FromRight } => DialogEntranceAnimationMode.SlideFromRight,
            SlideTransition => DialogEntranceAnimationMode.SlideFromBottom,
            _ => DialogEntranceAnimationMode.Entrance
        };
    }

    private static (ScaleTransform Scale, TranslateTransform Translate) EnsureDialogTransforms(ContentDialog dialog)
    {
        if (dialog.RenderTransform is TransformGroup existingGroup
            && !existingGroup.IsFrozen
            && existingGroup.Children.Count == 2
            && existingGroup.Children[0] is ScaleTransform existingScale
            && !existingScale.IsFrozen
            && existingGroup.Children[1] is TranslateTransform existingTranslate
            && !existingTranslate.IsFrozen)
        {
            dialog.RenderTransformOrigin = new Point(0.5, 0.5);
            return (existingScale, existingTranslate);
        }

        var scale = new ScaleTransform(1, 1);
        var translate = new TranslateTransform(0, 0);
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(translate);

        dialog.RenderTransform = group;
        dialog.RenderTransformOrigin = new Point(0.5, 0.5);

        return (scale, translate);
    }

    private static void StopDialogAnimations(ContentDialog dialog, ScaleTransform scale, TranslateTransform translate)
    {
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        translate.BeginAnimation(TranslateTransform.XProperty, null);
        translate.BeginAnimation(TranslateTransform.YProperty, null);
        dialog.BeginAnimation(UIElement.OpacityProperty, null);
    }

    private static void ResetDialogToRestState(ContentDialog dialog)
    {
        var (scale, translate) = EnsureDialogTransforms(dialog);
        ResetDialogToRestState(dialog, scale, translate);
    }

    private static void ResetDialogToRestState(ContentDialog dialog, ScaleTransform scale, TranslateTransform translate)
    {
        StopDialogAnimations(dialog, scale, translate);
        scale.ScaleX = 1;
        scale.ScaleY = 1;
        translate.X = 0;
        translate.Y = 0;
        dialog.Opacity = 1;
    }

    private static void AnimateBackdropOpen(ContentDialog dialog, bool shouldAnimate)
    {
        var backdrop = FindBackdropElement(dialog);
        if (backdrop is null)
            return;

        var targetOpacity = backdrop.Opacity;
        if (targetOpacity <= 0)
            targetOpacity = 1;

        backdrop.BeginAnimation(UIElement.OpacityProperty, null);

        if (!shouldAnimate)
        {
            backdrop.Opacity = targetOpacity;
            return;
        }

        backdrop.Opacity = 0;

        var backdropFadeAnimation = new DoubleAnimation(0, targetOpacity, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        backdropFadeAnimation.Completed += (_, _) => backdrop.Opacity = targetOpacity;

        backdrop.BeginAnimation(UIElement.OpacityProperty, backdropFadeAnimation, HandoffBehavior.SnapshotAndReplace);
    }

    private static UIElement? FindBackdropElement(ContentDialog dialog)
    {
        if (dialog.DialogHostEx is not DependencyObject host)
            return null;

        DependencyObject? current = dialog;
        while (current is not null && !ReferenceEquals(current, host))
        {
            var parent = VisualTreeHelper.GetParent(current);
            if (parent is Panel panel && panel.Children.Count > 1)
            {
                foreach (var child in panel.Children.OfType<UIElement>())
                {
                    if (ReferenceEquals(child, current))
                        continue;

                    if (child.Visibility != Visibility.Visible)
                        continue;

                    if (IsAncestorOf(child, dialog))
                        continue;

                    return child;
                }
            }

            current = parent;
        }

        return null;
    }

    private static bool IsAncestorOf(DependencyObject candidateAncestor, DependencyObject element)
    {
        DependencyObject? current = element;
        while (current is not null)
        {
            if (ReferenceEquals(current, candidateAncestor))
                return true;

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typed)
                return typed;

            var nested = FindDescendant<T>(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    /// <summary>
    /// Waits briefly for a <see cref="ContentDialogHost" /> to become available.
    /// Useful during startup when the main window visual tree isn't fully ready yet.
    /// </summary>
    public static async Task<bool> EnsureDialogHostAsync(ContentDialog dialog, int attempts = 20, int delayMilliseconds = 50)
    {
        if (attempts < 1)
            attempts = 1;

        if (delayMilliseconds < 1)
            delayMilliseconds = 1;

        for (var index = 0; index < attempts; index++)
        {
            if (SetupDialogHost(dialog))
                return true;

            await Task.Delay(delayMilliseconds);
        }

        return false;
    }

    /// <summary>
    /// Shows a configurable action dialog with optional confirm, cancel and custom buttons.
    /// </summary>
    public static async Task<DialogActionOutcome> ShowActionDialogAsync(DialogActionRequest request)
    {
        var dialog = new ActionDialog(request);

        if (request.ConfirmButton is not null)
        {
            dialog.PrimaryButtonText = request.ConfirmButton.Text;
            dialog.PrimaryButtonAppearance = request.ConfirmButton.Appearance;
        }

        if (request.CustomButton is not null)
        {
            dialog.SecondaryButtonText = request.CustomButton.Text;
            dialog.SecondaryButtonAppearance = request.CustomButton.Appearance;
        }

        if (request.CancelButton is not null)
            dialog.CloseButtonText = request.CancelButton.Text;

        var hostReady = await EnsureDialogHostAsync(dialog);
        if (!hostReady)
        {
            if (request.CancelButton?.CallbackAsync is not null)
                await request.CancelButton.CallbackAsync();

            return DialogActionOutcome.Cancelled;
        }

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary && request.ConfirmButton is not null)
        {
            if (request.ConfirmButton.CallbackAsync is not null)
                await request.ConfirmButton.CallbackAsync();

            return DialogActionOutcome.Confirmed;
        }

        if (result == ContentDialogResult.Secondary && request.CustomButton is not null)
        {
            if (request.CustomButton.CallbackAsync is not null)
                await request.CustomButton.CallbackAsync();

            return DialogActionOutcome.Custom;
        }

        if (request.CancelButton?.CallbackAsync is not null)
            await request.CancelButton.CallbackAsync();

        return DialogActionOutcome.Cancelled;
    }
}
