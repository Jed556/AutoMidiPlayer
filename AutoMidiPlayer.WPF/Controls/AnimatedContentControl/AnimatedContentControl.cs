using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AutoMidiPlayer.WPF.Animation;
using AutoMidiPlayer.WPF.ViewModels;

namespace AutoMidiPlayer.WPF.Controls;

public class AnimatedContentControl : ContentControl
{
    private static Transition Transition => SettingsPageViewModel.Transition!.Object;

    protected override void OnContentChanged(object? oldContent, object? newContent)
    {
        if (oldContent != null)
        {
            var exit = Transition.GetExitAnimation(oldContent, false);
            try
            {
                exit?.Begin();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AnimatedContentControl] Exit animation error: {ex}");
            }
        }

        AutoMidiPlayer.WPF.Animation.Animation? enter = null;
        if (newContent != null)
        {
            enter = Transition.GetEnterAnimation(newContent, false);
            if (enter != null && newContent is UIElement uiElement1)
            {
                // Hide it before it renders so we don't see a flash of the final state
                uiElement1.Visibility = Visibility.Hidden;
            }
        }

        // Resolve the new view synchronously so the visual tree is fully constructed
        base.OnContentChanged(oldContent, newContent);

        if (enter != null && newContent is UIElement uiElement2)
        {
            // Defer the enter animation until after layout/measure is complete,
            // so the animation plays smoothly instead of being frozen by heavy view construction.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                // If we navigated away before this dispatcher ran, abort the animation
                // to prevent InvalidOperationException since the element is no longer loaded.
                if (Content != newContent)
                    return;

                try
                {
                    uiElement2.Visibility = Visibility.Visible;
                    enter.Begin();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AnimatedContentControl] Enter animation error: {ex}");
                }
            });
        }
    }
}
