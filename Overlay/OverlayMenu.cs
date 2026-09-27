using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using TaskbarMonitor.Core;
using TaskbarMonitor.Theming;
using TaskbarMonitor.ViewModels;

namespace TaskbarMonitor.Overlay;

/// <summary>
/// Right-click menu of the overlay. It follows the taskbar's theme (Windows mode), not the app mode,
/// so it looks like the taskbar's own menus even with the "Custom" color mode.
/// </summary>
internal sealed class OverlayMenu
{
    private const string FluentLight = "pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.Light.xaml";
    private const string FluentDark = "pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.Dark.xaml";

    private readonly ContextMenu _menu;
    private readonly Dictionary<bool, ResourceDictionary?> _themes = [];
    private Window? _host;
    private bool? _appliedLight;

    public OverlayMenu(SettingsViewModel vm)
    {
        _menu = new ContextMenu { DataContext = vm };
        _menu.Items.Add(new MenuItem { Header = "Настройки…", FontWeight = FontWeights.SemiBold, Command = vm.OpenSettingsCommand, Icon = Icon("") });
        _menu.Items.Add(new MenuItem { Header = "Диспетчер задач", Command = vm.OpenTaskManagerCommand, Icon = Icon("") });
        var update = new MenuItem { Command = vm.DownloadUpdateCommand, Icon = Icon("") };
        update.SetBinding(HeaderedItemsControl.HeaderProperty, nameof(SettingsViewModel.UpdateMenuText));
        update.SetBinding(UIElement.VisibilityProperty,
            new Binding(nameof(SettingsViewModel.UpdateAvailable)) { Converter = new BooleanToVisibilityConverter() });
        _menu.Items.Add(update);
        _menu.Items.Add(new Separator());
        _menu.Items.Add(CheckItem("Поверх всех окон", nameof(SettingsViewModel.AlwaysOnTop)));
        _menu.Items.Add(CheckItem("Скрывать в полноэкранном режиме", nameof(SettingsViewModel.HideOnFullscreen)));
        _menu.Items.Add(CheckItem("Закрепить позицию", nameof(SettingsViewModel.LockPosition)));
        _menu.Items.Add(CheckItem("Привязать к панели задач", nameof(SettingsViewModel.SnapToTaskbar)));
        _menu.Items.Add(new Separator());
        _menu.Items.Add(new MenuItem { Header = "Выход", Command = vm.ExitCommand, Icon = Icon("") });
        _menu.Closed += (_, _) => _host?.Hide();
    }

    private static MenuItem CheckItem(string header, string path)
    {
        var item = new MenuItem { Header = header, IsCheckable = true };
        item.SetBinding(MenuItem.IsCheckedProperty, new Binding(path) { Mode = BindingMode.TwoWay });
        return item;
    }

    private static TextBlock Icon(string glyph) => new()
    {
        Text = glyph,
        FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
        FontSize = 14,
    };

    public void Show(TaskbarSurface surface)
    {
        ApplyTheme(surface);
        _host ??= CreateHost();
        _host.Show();
        _host.Activate();
        Native.SetForegroundWindow(new WindowInteropHelper(_host).Handle);

        _menu.PlacementTarget = _host;
        _menu.Placement = PlacementMode.MousePoint;
        _menu.IsOpen = true;
    }

    private void ApplyTheme(TaskbarSurface surface)
    {
        // High contrast keeps the app theme: WPF's Fluent HC dictionary already follows system colors.
        bool? light = surface switch
        {
            TaskbarSurface.Light => true,
            TaskbarSurface.Dark or TaskbarSurface.Accent => false,
            _ => null,
        };
        if (light == _appliedLight)
            return;
        _appliedLight = light;
        _menu.Resources.MergedDictionaries.Clear();
        if (light is { } isLight && Load(isLight) is { } dictionary)
            _menu.Resources.MergedDictionaries.Add(dictionary);
    }

    private ResourceDictionary? Load(bool light)
    {
        if (_themes.TryGetValue(light, out var cached))
            return cached;
        ResourceDictionary? dictionary = null;
        try
        {
            dictionary = new ResourceDictionary { Source = new Uri(light ? FluentLight : FluentDark, UriKind.Absolute) };
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.Windows.Markup.XamlParseException)
        {
            ErrorLog.Write(ex);
        }
        _themes[light] = dictionary;
        return dictionary;
    }

    // A context menu only closes on outside clicks when its app owns the foreground,
    // while the overlay itself must never take focus: an invisible host window is activated instead.
    private static Window CreateHost() => new()
    {
        Width = 1,
        Height = 1,
        Left = -32000,
        Top = -32000,
        WindowStyle = WindowStyle.None,
        ResizeMode = ResizeMode.NoResize,
        ShowInTaskbar = false,
        ShowActivated = true,
        AllowsTransparency = true,
        Background = System.Windows.Media.Brushes.Transparent,
        Topmost = true,
        Title = "",
    };
}
