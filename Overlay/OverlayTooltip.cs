using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using TaskbarMonitor.Controls;
using TaskbarMonitor.Core;
using TaskbarMonitor.Telemetry;
using TaskbarMonitor.Theming;

namespace TaskbarMonitor.Overlay;

/// <summary>
/// Details shown while the pointer rests on the overlay: exact amounts and the busiest processes.
/// A window of its own rather than a WPF ToolTip: it has to sit above the (topmost) taskbar,
/// never take focus and let every click through to the overlay underneath.
/// </summary>
internal sealed class OverlayTooltip : Window
{
    // Transparent margin around the card that the drop shadow is drawn into.
    private const double ShadowRoom = 12;
    private const double Gap = 6;
    private const string Nbsp = " ";

    private static readonly Duration FadeIn = new(TimeSpan.FromMilliseconds(120));

    private readonly StackPanel _rows = new();
    private IntPtr _hwnd;
    private IntPtr _anchor;
    private bool? _appliedLight;

    public OverlayTooltip()
    {
        Title = "TaskBar Monitor — подробности";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Focusable = false;
        IsHitTestVisible = false;
        UseLayoutRounding = true;
        Left = -32000;
        Top = -32000;
        SetResourceReference(FontFamilyProperty, "AppFontFamily");
        FontSize = 12.5;

        var card = new Border
        {
            Margin = new Thickness(ShadowRoom),
            Padding = new Thickness(14, 10, 14, 12),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            MinWidth = 230,
            Child = _rows,
            Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Direction = 270, Opacity = 0.22, Color = Colors.Black },
        };
        card.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorQuarternaryBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "SurfaceStrokeColorFlyoutBrush");
        Content = card;
        SizeChanged += (_, _) => Place();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        Native.SetExStyle(_hwnd, Native.GetExStyle(_hwnd) | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT);
    }

    public void ShowFor(IntPtr overlay, TaskbarSurface surface)
    {
        _anchor = overlay;
        TaskbarFluentTheme.Apply(Resources.MergedDictionaries, surface, ref _appliedLight);
        if (!IsVisible)
        {
            bool animate = Motion.Enabled;
            BeginAnimation(OpacityProperty, null);
            Opacity = animate ? 0 : 1;
            Show();
            if (animate)
                BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, FadeIn));
        }
        Place();
    }

    public void Dismiss()
    {
        if (IsVisible)
            Hide();
    }

    public void Update(MetricsSnapshot s, AppSettings settings)
    {
        _rows.Children.Clear();
        var on = settings.Metrics.Where(m => m.Enabled).Select(m => m.Kind).ToHashSet();

        if (on.Contains(MetricKind.Cpu))
            Row("Процессор", Percent(s[MetricKind.Cpu]));
        if (s.RamTotalGb > 0)
            Row("Память", $"{Gb(s.RamUsedGb)} из {Gb(s.RamTotalGb)}{Nbsp}ГБ · {Percent(100 * s.RamUsedGb / s.RamTotalGb)}");

        if (on.Contains(MetricKind.Gpu) || on.Contains(MetricKind.GpuTemp))
        {
            var parts = new List<string>();
            if (on.Contains(MetricKind.Gpu))
                parts.Add(Percent(s[MetricKind.Gpu]));
            if (on.Contains(MetricKind.GpuTemp))
                parts.Add(double.IsNaN(s[MetricKind.GpuTemp]) ? "—" : $"{s[MetricKind.GpuTemp]:0}{Nbsp}°C");
            Row("Видеокарта", string.Join(" · ", parts));
            if (s.GpuName.Length > 0)
                Caption(s.GpuName);
        }
        if (on.Contains(MetricKind.Vram))
            Row("Видеопамять", s.VramTotalGb > 0
                ? $"{Gb(s.VramUsedGb)} из {Gb(s.VramTotalGb)}{Nbsp}ГБ · {Percent(s[MetricKind.Vram])}"
                : Percent(s[MetricKind.Vram]));

        if (on.Contains(MetricKind.NetUp) || on.Contains(MetricKind.NetDown))
        {
            var parts = new List<string>();
            if (on.Contains(MetricKind.NetUp))
                parts.Add($"↑{Nbsp}{Speed(s[MetricKind.NetUp])}");
            if (on.Contains(MetricKind.NetDown))
                parts.Add($"↓{Nbsp}{Speed(s[MetricKind.NetDown])}");
            Row("Сеть", string.Join("   ", parts));
        }
        if (on.Contains(MetricKind.DiskActivity))
            Row("Активность диска", Percent(s[MetricKind.DiskActivity]));
        if (on.Contains(MetricKind.DiskSpace))
            Row($"Диск {DriveName(settings.SpaceDrive)}", s.DiskTotalGb > 0
                ? $"свободно {Gb(s.DiskFreeGb)} из {Gb(s.DiskTotalGb)}{Nbsp}ГБ"
                : "—");

        Separator();
        Header("Больше всего нагружают процессор");
        if (s.TopProcesses is not { } top)
            Caption("Считаю…");
        else if (top.Count == 0)
            Caption("Процессор почти не занят");
        else
            foreach (var p in top)
                Row(p.Count > 1 ? $"{p.Name} ({p.Count})" : p.Name, ProcessPercent(p.CpuPercent));
    }

    private void Row(string label, string value)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var name = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        var amount = new TextBlock
        {
            Text = value,
            FontWeight = FontWeights.Medium,
            Margin = new Thickness(24, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        amount.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        Grid.SetColumn(amount, 1);
        grid.Children.Add(name);
        grid.Children.Add(amount);
        _rows.Children.Add(grid);
    }

    private void Caption(string text)
    {
        var caption = new TextBlock
        {
            Text = text,
            FontSize = 11.5,
            MaxWidth = 300,
            HorizontalAlignment = HorizontalAlignment.Left,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, 3),
        };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
        _rows.Children.Add(caption);
    }

    private void Header(string text)
    {
        var header = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) };
        header.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        _rows.Children.Add(header);
    }

    private void Separator()
    {
        var line = new Border { Height = 1, Margin = new Thickness(0, 8, 0, 8) };
        line.SetResourceReference(Border.BackgroundProperty, "DividerStrokeColorDefaultBrush");
        _rows.Children.Add(line);
    }

    private static string Percent(double v) => double.IsNaN(v) ? "—" : $"{Math.Round(v):0}{Nbsp}%";

    private static string ProcessPercent(double v) => v switch
    {
        < 0.05 => "<" + Nbsp + 0.1.ToString("0.0", CultureInfo.CurrentCulture) + Nbsp + "%",
        < 10 => v.ToString("0.0", CultureInfo.CurrentCulture) + Nbsp + "%",
        _ => $"{Math.Round(v):0}{Nbsp}%",
    };

    private static string Gb(double v) => double.IsNaN(v)
        ? "—"
        : v.ToString(v < 100 ? "0.0" : "0", CultureInfo.CurrentCulture);

    private static string Speed(double bytesPerSecond)
    {
        if (double.IsNaN(bytesPerSecond))
            return "—";
        double kb = bytesPerSecond / 1024;
        if (kb < 999.5)
            return $"{kb:0}{Nbsp}КБ/с";
        double mb = kb / 1024;
        if (mb < 999.5)
            return mb.ToString(mb < 100 ? "0.0" : "0", CultureInfo.CurrentCulture) + Nbsp + "МБ/с";
        return (mb / 1024).ToString("0.0", CultureInfo.CurrentCulture) + Nbsp + "ГБ/с";
    }

    private static string DriveName(string drive) =>
        (string.IsNullOrEmpty(drive) ? "C" : drive[..1].ToUpperInvariant()) + ":";

    // Centered over the overlay, with the card just above the taskbar; below it when there is no room above.
    private void Place()
    {
        if (_hwnd == IntPtr.Zero || _anchor == IntPtr.Zero || !IsVisible)
            return;
        if (!Native.GetWindowRect(_hwnd, out var self) || !Native.GetWindowRect(_anchor, out var overlay))
            return;
        var info = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfo(Native.MonitorFromWindow(_anchor, Native.MONITOR_DEFAULTTONEAREST), ref info))
            return;

        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        int room = (int)Math.Round(ShadowRoom * scale);
        int gap = (int)Math.Round(Gap * scale);
        var screen = info.rcMonitor;

        int x = (overlay.Left + overlay.Right - self.Width) / 2;
        x = Math.Clamp(x, screen.Left - room, Math.Max(screen.Left - room, screen.Right + room - self.Width));
        int y = overlay.Top - gap - self.Height + room;
        if (y + room < screen.Top)
            y = overlay.Bottom + gap - room;
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }
}
