using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Linq;

namespace AutoMidiPlayer.WPF.Controls;

public partial class DiscoveryCard : UserControl
{
    private static readonly Random s_rng = new();
    private static readonly System.Collections.Generic.HashSet<string> s_animatedIds = new();

    public DiscoveryCard()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is Services.MidiShow.MidiShowItem { IsLoading: true })
        {
            RandomizeSkeletonWidths();
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged oldNotify)
        {
            oldNotify.PropertyChanged -= OnItemPropertyChanged;
        }

        if (DataContext is Services.MidiShow.MidiShowItem item)
        {
            item.PropertyChanged += OnItemPropertyChanged;

            bool oldWasLoading = e.OldValue is Services.MidiShow.MidiShowItem oldItem && oldItem.IsLoading;
            bool isNewlyLoaded = !item.IsLoading && !string.IsNullOrEmpty(item.Id) && !s_animatedIds.Contains(item.Id) && oldWasLoading;
            if (isNewlyLoaded)
            {
                s_animatedIds.Add(item.Id);
            }

            // Reset avatar fade state for new data context.
            if (FindName("AvatarImageEllipse") is Ellipse ellipse)
            {
                ellipse.BeginAnimation(UIElement.OpacityProperty, null);
                ellipse.Opacity = 0;
            }

            ResetAllAnimationsAndSkeletons();
            
            var meta = FindName("MetaContent") as FrameworkElement;
            if (meta != null) meta.BeginAnimation(UIElement.OpacityProperty, null);

            var tags = FindName("TagsContent") as FrameworkElement;
            if (tags != null) tags.BeginAnimation(UIElement.OpacityProperty, null);

            if (item.IsLoading)
            {
                RandomizeSkeletonWidths();
            }

            if (isNewlyLoaded)
            {
                var zeroAnim = new DoubleAnimation(0, 0, TimeSpan.Zero);
                if (FindName("TitleText") is FrameworkElement t) t.BeginAnimation(UIElement.OpacityProperty, zeroAnim);
                if (FindName("DescText") is FrameworkElement d) d.BeginAnimation(UIElement.OpacityProperty, zeroAnim);
                if (meta != null) meta.BeginAnimation(UIElement.OpacityProperty, zeroAnim);
                if (tags != null) tags.BeginAnimation(UIElement.OpacityProperty, zeroAnim);

                Dispatcher.BeginInvoke(() => {
                    SetupAvatarFadeIn(true);
                    AnimateTextContent();
                }, System.Windows.Threading.DispatcherPriority.Loaded);
            }
            else if (!item.IsLoading)
            {
                Dispatcher.BeginInvoke(() => SetupAvatarFadeIn(false), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }
    }

    private void ResetAllAnimationsAndSkeletons()
    {
        void ResetElement(string name)
        {
            if (FindName(name) is FrameworkElement el)
            {
                el.BeginAnimation(FrameworkElement.WidthProperty, null);
                el.BeginAnimation(UIElement.OpacityProperty, null);
                el.BeginAnimation(UIElement.VisibilityProperty, null);
                el.Opacity = 1;
            }
        }

        ResetElement("TitleText");
        ResetElement("DescText");
        ResetElement("SkeletonTitle");
        ResetElement("SkeletonDesc");
        ResetElement("SkeletonMetaWrap");
        ResetElement("SkeletonTagsWrap");

        void CleanupDynamicSkeletons(string wrapName, int initialCount)
        {
            if (FindName(wrapName) is WrapPanel wp)
            {
                while (wp.Children.Count > initialCount)
                    wp.Children.RemoveAt(wp.Children.Count - 1);
                    
                foreach (UIElement child in wp.Children)
                {
                    child.BeginAnimation(FrameworkElement.WidthProperty, null);
                    child.BeginAnimation(UIElement.OpacityProperty, null);
                    if (child is FrameworkElement fe) fe.Opacity = 1;
                }
            }
        }

        CleanupDynamicSkeletons("SkeletonMetaWrap", 6); // 6 initial skeletons for meta
        CleanupDynamicSkeletons("SkeletonTagsWrap", 2); // 2 initial skeletons for tags
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (DataContext is Services.MidiShow.MidiShowItem item)
        {
            if (e.PropertyName == nameof(Services.MidiShow.MidiShowItem.IsLoading))
            {
                if (item.IsLoading)
                {
                    ResetAllAnimationsAndSkeletons();
                    RandomizeSkeletonWidths();
                }
            }
            else if (e.PropertyName == nameof(Services.MidiShow.MidiShowItem.IsLoadingDetails))
            {
                if (!item.IsLoadingDetails && item.Details?.HasBpm == true)
                {
                    Dispatcher.BeginInvoke(() => {
                        if (FindName("BpmContent") is FrameworkElement bpm && FindName("SkeletonBpm") is FrameworkElement skel)
                        {
                            AnimateWidthAndFadeIn(bpm, skel);
                        }
                    }, System.Windows.Threading.DispatcherPriority.Loaded);
                }
            }
        }
    }

    private BitmapImage? _subscribedBitmap;

    private void SetupAvatarFadeIn(bool forceAnimate)
    {
        // Unsubscribe from previous bitmap
        if (_subscribedBitmap is not null)
        {
            _subscribedBitmap.DownloadCompleted -= OnAvatarDownloadCompleted;
            _subscribedBitmap = null;
        }

        if (FindName("AvatarImageEllipse") is not Ellipse ellipse)
            return;

        if (FindName("AvatarBrush") is not ImageBrush brush)
            return;

        if (brush.ImageSource is BitmapImage bitmap)
        {
            if (bitmap.IsDownloading)
            {
                // Image still downloading — hide and subscribe for fade-in when done
                ellipse.BeginAnimation(UIElement.OpacityProperty, null); // clear any running animation
                ellipse.Opacity = 0;
                _subscribedBitmap = bitmap;
                bitmap.DownloadCompleted += OnAvatarDownloadCompleted;
            }
            else
            {
                // Already cached/downloaded
                if (forceAnimate)
                {
                    ellipse.Opacity = 0;
                    var anim = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(500)))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    ellipse.BeginAnimation(UIElement.OpacityProperty, anim);
                }
                else
                {
                    ellipse.BeginAnimation(UIElement.OpacityProperty, null);
                    ellipse.Opacity = 1;
                }
            }
        }
        else if (brush.ImageSource is not null)
        {
            // Non-BitmapImage source (already available)
            if (forceAnimate)
            {
                ellipse.Opacity = 0;
                var anim = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(500)))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                ellipse.BeginAnimation(UIElement.OpacityProperty, anim);
            }
            else
            {
                ellipse.BeginAnimation(UIElement.OpacityProperty, null);
                ellipse.Opacity = 1;
            }
        }
        else
        {
            // No image source — hide (music note icon shows)
            ellipse.BeginAnimation(UIElement.OpacityProperty, null);
            ellipse.Opacity = 0;
        }
    }

    private void OnAvatarDownloadCompleted(object? sender, EventArgs e)
    {
        if (sender is BitmapImage bitmap)
            bitmap.DownloadCompleted -= OnAvatarDownloadCompleted;
        _subscribedBitmap = null;

        if (FindName("AvatarImageEllipse") is Ellipse ellipse)
        {
            // Animate fade-in only when a download just finished
            var anim = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromSeconds(0.3)));
            ellipse.BeginAnimation(UIElement.OpacityProperty, anim);
        }
    }



    private void AnimateTextContent()
    {
        var duration = new Duration(TimeSpan.FromSeconds(0.4));
        var easing = new QuarticEase { EasingMode = EasingMode.EaseOut };

        System.Collections.Generic.List<double> GetTargetWidths(UIElement root)
        {
            var widths = new System.Collections.Generic.List<double>();
            void Traverse(DependencyObject node)
            {
                if (node is UIElement ui && ui.Visibility != Visibility.Visible) return;
                
                // For MetaContent, the items are StackPanels.
                if (node is StackPanel sp && sp.Parent is WrapPanel wp && wp.Name == "MetaContent")
                {
                    widths.Add(sp.ActualWidth);
                    return; // stop here, don't traverse inside the StackPanel
                }
                
                // For TagsContent, the category is a Border directly in TagsContent.
                if (node is Border b && b.Parent is DockPanel dp && dp.Name == "TagsContent")
                {
                    widths.Add(b.ActualWidth);
                    return;
                }

                // For TagContainer, the tags are Borders inside a WrapPanel.
                if (node is Border tb && tb.Padding.Left == 9 && tb.CornerRadius.TopLeft == 9)
                {
                    // This uniquely identifies a tag chip
                    widths.Add(tb.ActualWidth);
                    return;
                }

                int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);
                for (int i = 0; i < count; i++)
                {
                    Traverse(System.Windows.Media.VisualTreeHelper.GetChild(node, i));
                }
            }
            
            if (root != null) Traverse(root);
            return widths;
        }

        void AnimateDynamicSkeletons(WrapPanel skeletonWrap, Panel realContent)
        {
            if (skeletonWrap == null || realContent == null) return;
            
            var targetWidths = GetTargetWidths(realContent);

            var textFadeIn = new DoubleAnimation(0, 1, duration) { BeginTime = duration.TimeSpan };
            var skeletonFadeOut = new DoubleAnimation(1, 0, duration) { BeginTime = duration.TimeSpan };
            
            // Also reset opacity for next time when completed
            skeletonFadeOut.Completed += (s, e) => {
                skeletonWrap.BeginAnimation(UIElement.VisibilityProperty, null);
                foreach (UIElement child in skeletonWrap.Children)
                {
                    child.BeginAnimation(UIElement.OpacityProperty, null);
                    child.BeginAnimation(FrameworkElement.WidthProperty, null);
                }
            };
            
            realContent.BeginAnimation(UIElement.OpacityProperty, textFadeIn);
            skeletonWrap.BeginAnimation(UIElement.OpacityProperty, skeletonFadeOut);

            if (targetWidths.Count == 0) return;

            // Make sure the skeleton wrap has enough skeletons by cloning the first one if needed
            var initialSkeletons = skeletonWrap.Children.Cast<UIElement>().OfType<FrameworkElement>().ToList();
            var templateSkeleton = initialSkeletons.FirstOrDefault();
            if (templateSkeleton == null) return;

            // Generate additional skeletons if needed
            for (int i = initialSkeletons.Count; i < targetWidths.Count; i++)
            {
                var clone = new AutoMidiPlayer.WPF.Controls.SkeletonBlock
                {
                    Width = targetWidths[i], // Start at target width as requested
                    Height = templateSkeleton.ActualHeight > 0 ? templateSkeleton.ActualHeight : double.NaN,
                    Margin = templateSkeleton.Margin,
                    Padding = ((AutoMidiPlayer.WPF.Controls.SkeletonBlock)templateSkeleton).Padding,
                    CornerRadius = ((AutoMidiPlayer.WPF.Controls.SkeletonBlock)templateSkeleton).CornerRadius,
                    Opacity = 0 // Will fade in
                };
                skeletonWrap.Children.Add(clone);
                initialSkeletons.Add(clone);
            }

            // Force the skeleton wrap to be visible during animation without overriding bindings permanently
            var visibilityAnim = new ObjectAnimationUsingKeyFrames { Duration = duration.TimeSpan + duration.TimeSpan };
            visibilityAnim.KeyFrames.Add(new DiscreteObjectKeyFrame(Visibility.Visible, TimeSpan.Zero));
            skeletonWrap.BeginAnimation(UIElement.VisibilityProperty, visibilityAnim);

            // Animate each skeleton
            for (int i = 0; i < initialSkeletons.Count; i++)
            {
                var skel = initialSkeletons[i];
                
                if (skel.Opacity == 0)
                {
                    // Additional skeleton: fade in while stretching others
                    var fadeInEarly = new DoubleAnimation(0, 1, duration);
                    skel.BeginAnimation(UIElement.OpacityProperty, fadeInEarly);
                    if (i < targetWidths.Count)
                    {
                        skel.Width = targetWidths[i]; // already at target width
                    }
                }
                else
                {
                    // Initial skeleton: animate width
                    double sourceWidth = skel.ActualWidth > 0 ? skel.ActualWidth : skel.Width;
                    if (double.IsNaN(sourceWidth)) sourceWidth = 100;
                    
                    if (i < targetWidths.Count)
                    {
                        var widthAnim = new DoubleAnimation(sourceWidth, targetWidths[i], duration) { EasingFunction = easing };
                        skel.BeginAnimation(FrameworkElement.WidthProperty, widthAnim);
                    }
                    // For excess skeletons (i >= targetWidths.Count), we don't animate the width to 0.
                    // Just let them fade out with the rest of the skeletonWrap to avoid a "shrinking cut".
                }
            }
        }

        if (FindName("TitleText") is FrameworkElement title && FindName("SkeletonTitle") is FrameworkElement titleSkel)
            AnimateWidthAndFadeIn(title, titleSkel);

        if (FindName("DescText") is FrameworkElement desc && FindName("SkeletonDesc") is FrameworkElement descSkel)
            AnimateWidthAndFadeIn(desc, descSkel);

        if (FindName("SkeletonMetaWrap") is WrapPanel metaSkel && FindName("MetaContent") is Panel metaReal)
            AnimateDynamicSkeletons(metaSkel, metaReal);

        if (FindName("SkeletonTagsWrap") is WrapPanel tagsSkel && FindName("TagsContent") is Panel tagsReal)
            AnimateDynamicSkeletons(tagsSkel, tagsReal);
    }

    private void AnimateWidthAndFadeIn(FrameworkElement element, FrameworkElement skeleton)
    {
        if (element == null || skeleton == null) return;
        
        var duration = new Duration(TimeSpan.FromSeconds(0.4));
        var easing = new QuarticEase { EasingMode = EasingMode.EaseOut };

        double targetWidth = element.ActualWidth;
        if (targetWidth <= 0) targetWidth = 100; // fallback

        // Force skeleton to remain visible during the transition without destroying bindings
        var visibilityAnim = new ObjectAnimationUsingKeyFrames { Duration = duration.TimeSpan + duration.TimeSpan };
        visibilityAnim.KeyFrames.Add(new DiscreteObjectKeyFrame(Visibility.Visible, TimeSpan.Zero));
        skeleton.BeginAnimation(UIElement.VisibilityProperty, visibilityAnim);
        
        double sourceWidth = skeleton.ActualWidth > 0 ? skeleton.ActualWidth : skeleton.Width;
        if (double.IsNaN(sourceWidth)) sourceWidth = 100;
        var widthAnim = new DoubleAnimation(sourceWidth, targetWidth, duration) { EasingFunction = easing };
        
        // Read base opacity so we animate back to what's defined in XAML (e.g. 0.85, 0.6)
        double targetOpacity = 1.0;
        if (element.Name == "TitleText") targetOpacity = 1.0; // Fallback, XAML uses 1.0 implicitly
        else if (element.Name == "DescText") targetOpacity = 0.6;
        else if (element.Name == "BpmContent") targetOpacity = 1.0;
        
        var textFadeIn = new DoubleAnimation(0, targetOpacity, duration) { BeginTime = duration.TimeSpan };
        var skeletonFadeOut = new DoubleAnimation(1, 0, duration) { BeginTime = duration.TimeSpan };
        
        skeletonFadeOut.Completed += (s, e) => {
            skeleton.BeginAnimation(FrameworkElement.WidthProperty, null);
            skeleton.BeginAnimation(UIElement.OpacityProperty, null);
            skeleton.BeginAnimation(UIElement.VisibilityProperty, null);
        };

        element.BeginAnimation(UIElement.OpacityProperty, textFadeIn);

        skeleton.BeginAnimation(FrameworkElement.WidthProperty, widthAnim);
        skeleton.BeginAnimation(UIElement.OpacityProperty, skeletonFadeOut);
    }


    private void RandomizeSkeletonWidths()
    {
        if (FindName("SkeletonTitle") is SkeletonBlock title)
            title.Width = s_rng.Next(140, 260);
        if (FindName("SkeletonDesc") is SkeletonBlock desc)
            desc.Width = s_rng.Next(160, 320);

        if (FindName("SkeletonTagsWrap") is WrapPanel tagsWrap)
        {
            int tagCount = s_rng.Next(1, 4);
            while (tagsWrap.Children.Count > tagCount)
                tagsWrap.Children.RemoveAt(tagsWrap.Children.Count - 1);
                
            var templateTag = tagsWrap.Children.Count > 0 ? tagsWrap.Children[0] as AutoMidiPlayer.WPF.Controls.SkeletonBlock : null;
            while (tagsWrap.Children.Count < tagCount && templateTag != null)
            {
                var clone = new AutoMidiPlayer.WPF.Controls.SkeletonBlock
                {
                    Height = templateTag.ActualHeight > 0 ? templateTag.ActualHeight : double.NaN,
                    Margin = templateTag.Margin,
                    Padding = templateTag.Padding,
                    CornerRadius = templateTag.CornerRadius,
                    Visibility = Visibility.Visible
                };
                tagsWrap.Children.Add(clone);
            }
            
            foreach (UIElement child in tagsWrap.Children)
            {
                if (child is AutoMidiPlayer.WPF.Controls.SkeletonBlock sb)
                    sb.Width = s_rng.Next(60, 110);
            }
        }

        if (FindName("SkeletonMetaWrap") is WrapPanel metaWrap)
        {
            foreach (UIElement child in metaWrap.Children)
            {
                if (child is AutoMidiPlayer.WPF.Controls.SkeletonBlock sb)
                    sb.Width = s_rng.Next(40, 60);
            }
        }
    }


    #region Routed Events

    public static readonly RoutedEvent CardClickEvent = EventManager.RegisterRoutedEvent(
        nameof(CardClick), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(DiscoveryCard));

    public event RoutedEventHandler CardClick
    {
        add => AddHandler(CardClickEvent, value);
        remove => RemoveHandler(CardClickEvent, value);
    }

    public static readonly RoutedEvent PreviewClickEvent = EventManager.RegisterRoutedEvent(
        nameof(PreviewClick), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(DiscoveryCard));

    public event RoutedEventHandler PreviewClick
    {
        add => AddHandler(PreviewClickEvent, value);
        remove => RemoveHandler(PreviewClickEvent, value);
    }

    public static readonly RoutedEvent AddToSongsClickEvent = EventManager.RegisterRoutedEvent(
        nameof(AddToSongsClick), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(DiscoveryCard));

    public event RoutedEventHandler AddToSongsClick
    {
        add => AddHandler(AddToSongsClickEvent, value);
        remove => RemoveHandler(AddToSongsClickEvent, value);
    }

    private void Card_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(CardClickEvent, this));
    }

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(PreviewClickEvent, this));
    }

    private void AddToSongs_Click(object sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(AddToSongsClickEvent, this));
    }

    private void VisitPage_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is Services.MidiShow.MidiShowItem item && !string.IsNullOrEmpty(item.PageUrl))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = item.PageUrl,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }

    #endregion
}
