using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TaskbarMonitor.Overlay;
using TaskbarMonitor.Theming;
using TaskbarMonitor.ViewModels;

namespace TaskbarMonitor.Controls;

/// <summary>
/// A strip that imitates the taskbar in one theme and renders the real overlay on it with live data,
/// so the automatic colors for every theme can be compared side by side.
/// </summary>
public sealed class OverlayPreview : Border
{
    public const double StripHeight = 48;

    public static readonly DependencyProperty SurfaceProperty = DependencyProperty.Register(
        nameof(Surface), typeof(TaskbarSurface), typeof(OverlayPreview),
        new PropertyMetadata(TaskbarSurface.Dark, (d, _) => ((OverlayPreview)d).Refresh(true)));

    private readonly OverlayView _view = new() { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, BarHeight = StripHeight };
    private readonly TextBlock _time = new() { FontSize = 12, TextAlignment = TextAlignment.Right };
    private readonly TextBlock _date = new() { FontSize = 12, TextAlignment = TextAlignment.Right };
    private readonly SolidColorBrush _strip = new(Colors.Black);
    private readonly SolidColorBrush _clock = new(Colors.White);
    private SettingsViewModel? _vm;

    public OverlayPreview()
    {
        Height = StripHeight;
        CornerRadius = new CornerRadius(6);
        ClipToBounds = true;
        Background = _strip;
        BorderThickness = new Thickness(1);
        SetResourceReference(BorderBrushProperty, "CardStrokeColorDefaultBrush");
        SnapsToDevicePixels = true;

        _time.Foreground = _clock;
        _date.Foreground = _clock;
        _time.FontFamily = _date.FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        var clock = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 14, 0), Children = { _time, _date } };
        var now = DateTime.Now;
        _time.Text = now.ToString("HH:mm");
        _date.Text = now.ToString("dd.MM.yyyy");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(_view);
        Grid.SetColumn(clock, 1);
        grid.Children.Add(clock);
        Child = grid;

        System.Windows.Automation.AutomationProperties.SetName(this, "Превью оверлея");
        DataContextChanged += (_, _) => Attach();
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    public TaskbarSurface Surface
    {
        get => (TaskbarSurface)GetValue(SurfaceProperty);
        set => SetValue(SurfaceProperty, value);
    }

    private void Attach()
    {
        var vm = DataContext as SettingsViewModel;
        if (ReferenceEquals(vm, _vm) || !IsLoaded)
            return;
        Detach();
        _vm = vm;
        if (_vm is null)
            return;
        _vm.OverlayChanged += OnSettingsChanged;
        _vm.SnapshotUpdated += OnSnapshot;
        Refresh(false);
    }

    private void Detach()
    {
        if (_vm is null)
            return;
        _vm.OverlayChanged -= OnSettingsChanged;
        _vm.SnapshotUpdated -= OnSnapshot;
        _vm = null;
    }

    private void OnSettingsChanged() => Refresh(true);

    private void OnSnapshot()
    {
        if (_vm is null)
            return;
        _view.Update(MetricLayout.Build(_vm.Snapshot, _vm.Settings), _vm.Settings);
    }

    private void Refresh(bool animate)
    {
        if (_vm is null)
            return;
        var palette = TaskbarPalette.Build(Surface, _vm.Theme, _vm.Settings);
        Fade(_strip, palette.Background, animate);
        Fade(_clock, palette.Value, animate);
        _view.ApplyPalette(palette, animate);
        _view.Update(MetricLayout.Build(_vm.Snapshot, _vm.Settings), _vm.Settings);
        _view.Relayout();
    }

    private static void Fade(SolidColorBrush brush, Color target, bool animate)
    {
        var from = brush.Color;
        brush.Color = target;
        brush.BeginAnimation(SolidColorBrush.ColorProperty,
            animate && from != target && SystemParameters.ClientAreaAnimation
                ? new ColorAnimation(from, target, new Duration(TimeSpan.FromMilliseconds(250))) { FillBehavior = FillBehavior.Stop }
                : null);
    }
}
