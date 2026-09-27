using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using TaskbarMonitor.Core;
using TaskbarMonitor.Theming;
using TaskbarMonitor.ViewModels;

namespace TaskbarMonitor.Overlay;

/// <summary>Right-click menu of the overlay, themed like the taskbar (see <see cref="TaskbarFluentTheme"/>).</summary>
internal sealed class OverlayMenu
{
    private readonly ContextMenu _menu;
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
        TaskbarFluentTheme.Apply(_menu.Resources.MergedDictionaries, surface, ref _appliedLight);
        _host ??= CreateHost();
        _host.Show();
        _host.Activate();
        Native.SetForegroundWindow(new WindowInteropHelper(_host).Handle);

        _menu.PlacementTarget = _host;
        _menu.Placement = PlacementMode.MousePoint;
        _menu.IsOpen = true;
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
