using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TaskbarMonitor.ViewModels;

namespace TaskbarMonitor.Views;

public partial class SettingsWindow : Window
{
    // ListBox items must keep reference equality: Page is assigned later and must not change the item's hash.
    private sealed class NavEntry(string glyph, string title, Func<UserControl> create)
    {
        public string Glyph { get; } = glyph;
        public string Title { get; } = title;
        public Func<UserControl> Create { get; } = create;
        public UserControl? Page { get; set; }

        // Screen readers announce list items by their text.
        public override string ToString() => Title;
    }

    private readonly NavEntry[] _entries =
    [
        new("", "Общие", () => new GeneralPage()),
        new("", "Метрики", () => new MetricsPage()),
        new("", "Оформление", () => new AppearancePage()),
        new("", "О программе", () => new AboutPage()),
    ];

    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Nav.ItemsSource = _entries;
        Nav.SelectedIndex = 0;
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
        Scroller.ScrollToTop();
    }
}
