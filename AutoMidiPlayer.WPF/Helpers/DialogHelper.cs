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
            ScrollEdgeFadeBehavior.SetIsEnabled(viewer, true);
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
                ScrollEdgeFadeBehavior.SetIsEnabled(deferredViewer, true);
            }));
    }

    private static ScrollViewer? ResolveDialogScrollViewer(ContentDialog dialog)
    {
        dialog.ApplyTemplate();

        if (dialog.Template?.FindName("PART_ScrollViewer", dialog) is ScrollViewer partScrollViewer)
            return partScrollViewer;

        // 1. The main ScrollViewer is often an ancestor of the dialog's Content (e.g. WPF UI wraps Content in a ScrollViewer).
        if (dialog.Content is DependencyObject contentElement)
        {
            var current = VisualTreeHelper.GetParent(contentElement);
            while (current != null && current != dialog)
            {
                if (current is ScrollViewer sv)
                    return sv;
                current = VisualTreeHelper.GetParent(current);
            }
        }

        // 2. If not found, search the visual tree top-down but SKIP the Content subtree!
        // This guarantees we don't accidentally find a ScrollViewer inside a TextBox.
        var queue = new System.Collections.Generic.Queue<DependencyObject>();
        queue.Enqueue(dialog);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            int count = VisualTreeHelper.GetChildrenCount(current);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(current, i);
                if (child is ScrollViewer viewer)
                    return viewer;

                // Skip traversing into the dialog's content subtree
                if (child != dialog.Content as DependencyObject)
                {
                    queue.Enqueue(child);
                }
            }
        }

        return null;
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

        var hostReady = await EnsureDialogHostAsync(dialog);
        if (!hostReady)
        {
            if (request.CancelButton?.CallbackAsync is not null)
                await request.CancelButton.CallbackAsync();

            return DialogActionOutcome.Cancelled;
        }

        var uiResult = await dialog.ShowAsync();
        var outcome = uiResult switch
        {
            ContentDialogResult.Primary => DialogActionOutcome.Confirmed,
            ContentDialogResult.Secondary => DialogActionOutcome.Custom,
            _ => DialogActionOutcome.Cancelled
        };

        if (outcome == DialogActionOutcome.Confirmed && request.ConfirmButton is not null)
        {
            if (request.ConfirmButton.CallbackAsync is not null)
                await request.ConfirmButton.CallbackAsync();

            return DialogActionOutcome.Confirmed;
        }

        if (outcome == DialogActionOutcome.Custom && request.CustomButton is not null)
        {
            if (request.CustomButton.CallbackAsync is not null)
                await request.CustomButton.CallbackAsync();

            return DialogActionOutcome.Custom;
        }

        if (request.CancelButton?.CallbackAsync is not null)
            await request.CancelButton.CallbackAsync();

        return DialogActionOutcome.Cancelled;
    }

    /// <summary>
    /// Hooks a ContentDialog's button to intercept its click event, preventing the dialog from closing.
    /// You must set e.Handled = true inside your callback to successfully prevent closure.
    /// </summary>
    public static void HookButtonToPreventClose(ContentDialog dialog, ContentDialogButton buttonType, System.Windows.Input.MouseButtonEventHandler previewClickCallback)
    {
        void InternalHook(object? sender, EventArgs e)
        {
            var button = FindDialogButton(dialog, buttonType);
            if (button != null)
            {
                button.PreviewMouseLeftButtonUp += (s, args) =>
                {
                    previewClickCallback(s, args);

                    // Reset IsPressed state manually so animations aren't broken by swallowing the event
                    if (args.Handled && s is System.Windows.Controls.Primitives.ButtonBase btn)
                    {
                        var isPressedProperty = typeof(System.Windows.Controls.Primitives.ButtonBase).GetProperty("IsPressed", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        if (isPressedProperty != null && isPressedProperty.CanWrite)
                        {
                            isPressedProperty.SetValue(btn, false);
                        }
                        else
                        {
                            // Fallback using reflection for protected setter
                            var method = typeof(System.Windows.Controls.Primitives.ButtonBase).GetMethod("set_IsPressed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                            if (method != null)
                                method.Invoke(btn, new object[] { false });
                        }
                    }
                };
            }
        }

        dialog.Loaded += (s, e) =>
        {
            InternalHook(s, e);
            
            // Fallback for late template application
            dialog.Dispatcher.BeginInvoke(new Action(() => InternalHook(s, e)), DispatcherPriority.Loaded);
        };
    }

    /// <summary>
    /// Hides the entire action buttons area (footer) of a ContentDialog.
    /// Useful for non-interactable dialogs like progress dialogs.
    /// </summary>
    public static void HideActionButtonsArea(ContentDialog dialog)
    {
        void InternalHide()
        {
            CollapseFooterElements(dialog);
        }

        dialog.Loaded += (s, e) =>
        {
            InternalHide();
            dialog.Dispatcher.BeginInvoke(new Action(InternalHide), DispatcherPriority.Loaded);
            dialog.Dispatcher.BeginInvoke(new Action(InternalHide), DispatcherPriority.Render);
        };
    }

    private static void CollapseFooterElements(DependencyObject root)
    {
        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            
            if (child is FrameworkElement fe)
            {
                // Look for the Grid that directly contains dialog buttons
                // In WPF-UI v4.x, buttons are named "PrimaryButton", "SecondaryButton", "CloseButton"
                if (fe is System.Windows.Controls.Panel panel && ContainsDialogButtons(panel))
                {
                    // Collapse the button grid itself
                    panel.Visibility = Visibility.Collapsed;
                    
                    // Collapse the parent Border wrapper (the footer area)
                    var parent = VisualTreeHelper.GetParent(panel) as FrameworkElement;
                    if (parent != null)
                    {
                        parent.Visibility = Visibility.Collapsed;
                    }
                    return;
                }
            }
            
            CollapseFooterElements(child);
        }
    }

    private static bool ContainsDialogButtons(System.Windows.Controls.Panel panel)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(panel); i++)
        {
            var child = VisualTreeHelper.GetChild(panel, i);
            if (child is System.Windows.Controls.Button btn && !string.IsNullOrEmpty(btn.Name))
            {
                // Match both v3 "PART_PrimaryButton" and v4 "PrimaryButton" naming
                if (btn.Name is "PrimaryButton" or "SecondaryButton" or "CloseButton" or
                    "PART_PrimaryButton" or "PART_SecondaryButton" or "PART_CloseButton")
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static System.Windows.Controls.Button? FindDialogButton(ContentDialog dialog, ContentDialogButton buttonType)
    {
        // WPF-UI v4.x uses plain names, v3.x uses PART_ prefixed names
        var v4Name = buttonType switch
        {
            ContentDialogButton.Primary => "PrimaryButton",
            ContentDialogButton.Secondary => "SecondaryButton",
            ContentDialogButton.Close => "CloseButton",
            _ => ""
        };
        
        var v3Name = buttonType switch
        {
            ContentDialogButton.Primary => "PART_PrimaryButton",
            ContentDialogButton.Secondary => "PART_SecondaryButton",
            ContentDialogButton.Close => "PART_CloseButton",
            _ => ""
        };

        // Try v4 name first
        if (dialog.Template?.FindName(v4Name, dialog) is System.Windows.Controls.Button btn4)
            return btn4;
            
        // Try v3 name
        if (dialog.Template?.FindName(v3Name, dialog) is System.Windows.Controls.Button btn3)
            return btn3;
        
        // Fall back to searching by name in the visual tree
        var foundByName = FindButtonByName(dialog, v4Name) ?? FindButtonByName(dialog, v3Name);
        if (foundByName != null)
            return foundByName;

        var expectedText = buttonType switch
        {
            ContentDialogButton.Primary => dialog.PrimaryButtonText,
            ContentDialogButton.Secondary => dialog.SecondaryButtonText,
            ContentDialogButton.Close => dialog.CloseButtonText,
            _ => ""
        };

        if (string.IsNullOrEmpty(expectedText)) 
            return null;

        return FindButtonByText(dialog, expectedText);
    }

    private static System.Windows.Controls.Button? FindButtonByName(DependencyObject root, string name)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is System.Windows.Controls.Button button && button.Name == name)
                return button;

            var nested = FindButtonByName(child, name);
            if (nested != null)
                return nested;
        }
        return null;
    }

    private static System.Windows.Controls.Button? FindButtonByText(DependencyObject root, string text)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is System.Windows.Controls.Button button)
            {
                if (string.Equals(button.Content?.ToString(), text, StringComparison.OrdinalIgnoreCase))
                    return button;
            }

            var nested = FindButtonByText(child, text);
            if (nested != null)
                return nested;
        }

        return null;
    }
}
