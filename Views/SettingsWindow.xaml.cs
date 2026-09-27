using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TaskbarMonitor.Controls;
using TaskbarMonitor.ViewModels;

namespace TaskbarMonitor.Views;

public partial class SettingsWindow : Window
{
    // ListBox items must keep reference equality: Page is assigned later and must not change the item's hash.
    private sealed class NavEntry(string glyph, string title, Func<UserControl> create, bool showsUpdates = false)
    {
        public string Glyph { get; } = glyph;
        public string Title { get; } = title;
        public Func<UserControl> Create { get; } = create;
        public bool ShowsUpdates { get; } = showsUpdates;
        public UserControl? Page { get; set; }

        // Screen readers announce list items by their text.
        public override string ToString() => Title;
    }

    private readonly NavEntry[] _entries =
    [
        new("", "Общие", () => new GeneralPage()),
        new("", "Метрики", () => new MetricsPage()),
        new("", "Оформление", () => new AppearancePage()),
        new("", "О программе", () => new AboutPage(), showsUpdates: true),
    ];

    private int _navigation;
    private bool _indicatorPlaced;

    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Nav.ItemsSource = _entries;
        Nav.SelectedIndex = 0;
        Loaded += (_, _) =>
        {
            MoveIndicator();
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, PrepareNextPage);
        };
    }

    // Esc closes the window unless an open drop-down or flyout already used it.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && !e.Handled)
        {
            e.Handled = true;
            Close();
        }
    }

    private void OnNavigate(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is not NavEntry entry)
            return;
        MoveIndicator();

        int navigation = ++_navigation;
        if (PageHost.Content is null || !Motion.Enabled)
        {
            ShowPage(entry);
            return;
        }

        // Page refresh, as in Windows Settings: the old page fades out quickly, the new one slides up into place.
        var exit = new DoubleAnimation(0, Motion.PageExit) { EasingFunction = Motion.Accelerate };
        exit.Completed += (_, _) =>
        {
            if (navigation != _navigation)
                return;
            ShowPage(entry);
            PageHost.Opacity = 0;
            PageShift.Y = Motion.PageOffset;
            // Start once the new page has been laid out, so building it does not eat the first frames.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (navigation != _navigation)
                    return;
                PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Motion.PageFade));
                PageShift.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(Motion.PageOffset, 0, Motion.PageEnter) { EasingFunction = Motion.Decelerate });
            });
        };
        PageHost.BeginAnimation(OpacityProperty, exit);
    }

    // Pages are built one per idle moment, so the first visit to a section does not stall its transition.
    private void PrepareNextPage()
    {
        if (!IsLoaded || _entries.FirstOrDefault(e => e.Page is null) is not { } entry)
            return;
        try
        {
            entry.Page = entry.Create();
        }
        catch (Exception ex)
        {
            Core.ErrorLog.Write(ex);
            return;
        }
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, PrepareNextPage);
    }

    private void ShowPage(NavEntry entry)
    {
        PageHost.BeginAnimation(OpacityProperty, null);
        PageShift.BeginAnimation(TranslateTransform.YProperty, null);
        PageHost.Opacity = 1;
        PageShift.Y = 0;
        try
        {
            entry.Page ??= entry.Create();
            PageHost.Content = entry.Page;
        }
        catch (Exception ex)
        {
            Core.ErrorLog.Write(ex);
            PageHost.Content = new TextBlock
            {
                Text = $"Не удалось открыть раздел «{entry.Title}». Подробности — в журнале ошибок:\n{Core.ErrorLog.FilePath}",
                TextWrapping = TextWrapping.Wrap,
            };
        }
        Scroller.JumpToTop();
    }

    // The pill glides to the selected item and stretches on the way, like NavigationView's indicator.
    private void MoveIndicator()
    {
        if (Nav.ItemContainerGenerator.ContainerFromItem(Nav.SelectedItem) is not ListBoxItem item || !item.IsLoaded)
            return;

        double y = item.TranslatePoint(new Point(0, (item.ActualHeight - NavIndicator.Height) / 2), NavLayer).Y;
        double x = item.TranslatePoint(new Point(0, 0), NavLayer).X;
        Canvas.SetLeft(NavIndicator, x);
        NavIndicator.Visibility = Visibility.Visible;

        if (!_indicatorPlaced || !Motion.Enabled)
        {
            _indicatorPlaced = true;
            IndicatorShift.BeginAnimation(TranslateTransform.YProperty, null);
            IndicatorStretch.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            IndicatorShift.Y = y;
            return;
        }

        IndicatorShift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(y, Motion.Indicator) { EasingFunction = Motion.Decelerate });

        var stretch = new DoubleAnimationUsingKeyFrames { Duration = Motion.Indicator };
        stretch.KeyFrames.Add(new EasingDoubleKeyFrame(1.9, KeyTime.FromPercent(0.3), new CubicEase { EasingMode = EasingMode.EaseOut }));
        stretch.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1), new CubicEase { EasingMode = EasingMode.EaseInOut }));
        IndicatorStretch.BeginAnimation(ScaleTransform.ScaleYProperty, stretch);
    }
}
