using System;
using System.Collections.Generic;
using System.Windows;
using AutoMidiPlayer.WPF.ViewModels;
using Stylet;

namespace AutoMidiPlayer.WPF.Helpers;

public class CachedViewManager : ViewManager
{
    private readonly Dictionary<Type, UIElement> _viewCache = new();

    public CachedViewManager(ViewManagerConfig config) : base(config)
    {
    }

    public override UIElement CreateViewForModel(object model)
    {
        if (model == null)
            return base.CreateViewForModel(model);

        var modelType = model.GetType();

        var settings = AutoMidiPlayer.Data.Properties.Settings.Default;
        bool isPageCachingEnabled = !settings.DebugModeEnabled || settings.PageCaching;

        // Only cache heavy main navigation pages to avoid memory leaks on dialogs
        bool shouldCache = isPageCachingEnabled &&
                           model is SettingsPageViewModel or SongsViewModel or QueueViewModel or TrackViewModel or PianoSheetViewModel or InstrumentViewModel or AboutViewModel;

        if (shouldCache && _viewCache.TryGetValue(modelType, out var cachedView))
        {
            return cachedView;
        }

        var view = base.CreateViewForModel(model);

        if (shouldCache && view != null)
        {
            if (view is FrameworkElement fe)
            {
                fe.DataContext = model;
            }
            _viewCache[modelType] = view;
        }

        return view!;
    }
}
