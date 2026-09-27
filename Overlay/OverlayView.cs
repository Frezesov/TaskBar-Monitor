using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TaskbarMonitor.Core;
using TaskbarMonitor.Theming;

namespace TaskbarMonitor.Overlay;

/// <summary>
/// Draws the metric columns directly in OnRender: no per-frame layout tree, and every brush is kept
/// (not frozen) so a theme change cross-fades the colors instead of snapping.
/// </summary>
public sealed class OverlayView : FrameworkElement
{
    private static readonly Duration ColorFade = new(TimeSpan.FromMilliseconds(250));

    // Fully transparent pixels of a layered window let clicks through; alpha 1/255 is invisible yet clickable.
    private static readonly Brush HitArea = Freeze(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)));

    private readonly SolidColorBrush _label = new(Colors.White);
    private readonly SolidColorBrush _value = new(Colors.White);
    private readonly SolidColorBrush _caution = new(Colors.Yellow);
    private readonly SolidColorBrush _critical = new(Colors.Red);
    private readonly SolidColorBrush _pod = new(Colors.Transparent);
    private readonly SolidColorBrush _podStroke = new(Colors.Transparent);
    private readonly SolidColorBrush _hover = new(Colors.Transparent);
    private readonly SolidColorBrush _plate = new(Colors.Transparent);
    private readonly Pen _podPen;

    private IReadOnlyList<MetricColumn> _columns = [];
    private AppSettings _settings = new();
    private Metrics? _metrics;
    private ColumnGeometry[] _geometry = [];

    public OverlayView()
    {
        _podPen = new Pen(_podStroke, 1);
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
    }

    /// <summary>Height of the strip the overlay sits in (the taskbar), in DIPs. NaN = fit the content.</summary>
    public double BarHeight
    {
        get;
        set
        {
            if (field.Equals(value))
                return;
            field = value;
            InvalidateMeasure();
        }
    } = double.NaN;

    public bool IsHot
    {
        get;
        set
        {
            if (field == value)
                return;
            field = value;
            InvalidateVisual();
        }
    }

    public OverlayPalette? Palette { get; private set; }

    public void Update(IReadOnlyList<MetricColumn> columns, AppSettings settings)
    {
        bool layoutChanged = !ReferenceEquals(settings, _settings) || _metrics is null || !SameShape(columns, _columns);
        _columns = columns;
        _settings = settings;
        if (layoutChanged)
            InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>Forces a full re-measure after a setting that affects size changed in place.</summary>
    public void Relayout()
    {
        _metrics = null;
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void ApplyPalette(OverlayPalette palette, bool animate)
    {
        Palette = palette;
        animate &= SystemParameters.ClientAreaAnimation;
        Fade(_label, palette.Label, animate);
        Fade(_value, palette.Value, animate);
        Fade(_caution, palette.Caution, animate);
        Fade(_critical, palette.Critical, animate);
        Fade(_pod, palette.Pod, animate);
        Fade(_podStroke, palette.PodStroke, animate);
        Fade(_hover, palette.Hover, animate);
        Fade(_plate, palette.Plate, animate);
        InvalidateVisual();
    }

    private static void Fade(SolidColorBrush brush, Color target, bool animate)
    {
        var from = brush.Color;
        brush.Color = target;
        if (!animate || from == target)
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            return;
        }
        brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(from, target, ColorFade)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        });
    }

    private static bool SameShape(IReadOnlyList<MetricColumn> a, IReadOnlyList<MetricColumn> b)
    {
        if (a.Count != b.Count)
            return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].Top.Kind != b[i].Top.Kind || a[i].Top.Label != b[i].Top.Label || a[i].Bottom?.Kind != b[i].Bottom?.Kind)
                return false;
            // A value wider than its reserve (unexpected units) must grow the column.
            if (a[i].Top.Value.Length > b[i].Top.Value.Length || (a[i].Bottom?.Value.Length ?? 0) > (b[i].Bottom?.Value.Length ?? 0))
                return false;
        }
        return true;
    }

    private sealed record Metrics(Typeface Typeface, double FontSize, double LineHeight, double Scale, double PixelsPerDip);

    private readonly record struct ColumnGeometry(double X, double Width, double LabelWidth, double ValueWidth);

    private Metrics GetMetrics()
    {
        double scale = _settings.ScalePercent / 100.0;
        var weight = _settings.FontWeight switch
        {
            TextWeight.Regular => FontWeights.Normal,
            TextWeight.Bold => FontWeights.Bold,
            _ => FontWeights.SemiBold,
        };
        var family = OverlayFonts.Resolve(_settings.FontFamily);
        double fontSize = (_settings.TwoRows ? 11.5 : 13) * scale;
        double lineHeight = Math.Ceiling(fontSize * family.LineSpacing);
        return new Metrics(new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal), fontSize, lineHeight, scale,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }

    private FormattedText Text(string text, Brush brush) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, _metrics!.Typeface, _metrics.FontSize, brush,
            null, TextFormattingMode.Display, _metrics.PixelsPerDip);

    private double TextWidth(string text) => Text(text, _value).WidthIncludingTrailingWhitespace;

    private double Pad => 7 * _metrics!.Scale;
    private double LabelGap => 5 * _metrics!.Scale;
    private double SparkGap => 5 * _metrics!.Scale;
    private double SparkWidth => 22 * _metrics!.Scale;
    private double OuterMargin => 2 * _metrics!.Scale;
    private bool InlineSparks => _settings.Sparklines == SparklineMode.Inline;

    private double ContentHeight(int rows) => rows * _metrics!.LineHeight;

    private double PodHeight(double barHeight)
    {
        int rows = _settings.TwoRows ? 2 : 1;
        double content = ContentHeight(rows);
        double wanted = content + 8 * _metrics!.Scale;
        return Math.Max(content, Math.Min(wanted, barHeight - 6));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _metrics = GetMetrics();
        _geometry = new ColumnGeometry[_columns.Count];
        double x = OuterMargin;
        double spacing = _settings.ColumnSpacing * _metrics.Scale;
        for (int i = 0; i < _columns.Count; i++)
        {
            var column = _columns[i];
            double labelWidth = column.Cells.Max(c => TextWidth(c.Label));
            double valueWidth = column.Cells.Max(c => Math.Max(c.ReserveValues.Max(TextWidth), TextWidth(c.Value)));
            double width = Pad * 2 + labelWidth + LabelGap + valueWidth + (InlineSparks ? SparkGap + SparkWidth : 0);
            width = Math.Ceiling(width);
            _geometry[i] = new ColumnGeometry(x, width, labelWidth, valueWidth);
            x += width + (i < _columns.Count - 1 ? spacing : 0);
        }
        x += OuterMargin;

        double height = double.IsNaN(BarHeight)
            ? Math.Ceiling(ContentHeight(_settings.TwoRows ? 2 : 1) + 12 * _metrics.Scale)
            : BarHeight;
        return new Size(_columns.Count == 0 ? 1 : Math.Ceiling(x), Math.Max(1, height));
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_metrics is null || _columns.Count == 0 || _geometry.Length != _columns.Count)
            return;

        var m = _metrics;
        double width = RenderSize.Width, height = RenderSize.Height;
        double podHeight = PodHeight(height);
        double podTop = Math.Round((height - podHeight) / 2);
        double outerRadius = 6 * m.Scale;

        dc.DrawRectangle(HitArea, null, new Rect(0, 0, width, height));

        if (_settings.ShowBackground)
            dc.DrawRoundedRectangle(_plate, null, new Rect(0, podTop - 1, width, podHeight + 2), outerRadius, outerRadius);
        if (IsHot)
            dc.DrawRoundedRectangle(_hover, null, new Rect(0, podTop - 1, width, podHeight + 2), outerRadius, outerRadius);

        int rows = _settings.TwoRows ? 2 : 1;
        double textTop = Math.Round(podTop + (podHeight - ContentHeight(rows)) / 2);

        for (int i = 0; i < _columns.Count; i++)
        {
            var column = _columns[i];
            var g = _geometry[i];
            var podRect = new Rect(g.X, podTop, g.Width, podHeight);
            double radius = 5 * m.Scale;

            if (_settings.ShowPods)
                dc.DrawRoundedRectangle(_pod, _podPen, Inset(podRect, 0.5), radius, radius);

            if (_settings.Sparklines == SparklineMode.Background)
            {
                dc.PushClip(new RectangleGeometry(podRect, radius, radius));
                var area = new Rect(podRect.X, podRect.Y + podRect.Height * 0.25, podRect.Width, podRect.Height * 0.75);
                DrawSparkline(dc, column.Top, area, fillOpacity: 0.16, lineOpacity: 0.32, dot: false);
                dc.Pop();
            }

            double y = textTop;
            if (column.Bottom is null && rows == 2)
                y = Math.Round(podTop + (podHeight - m.LineHeight) / 2);
            foreach (var cell in column.Cells)
            {
                DrawCell(dc, cell, g, y);
                y += m.LineHeight;
            }
        }
    }

    private void DrawCell(DrawingContext dc, MetricCell cell, ColumnGeometry g, double y)
    {
        var m = _metrics!;
        double x = g.X + Pad;
        dc.DrawText(Text(cell.Label, _label), Snap(new Point(x, y)));

        var valueBrush = cell.Severity switch
        {
            Severity.Critical => _critical,
            Severity.Caution => _caution,
            _ => _value,
        };
        var value = Text(cell.Value, valueBrush);
        double valueRight = x + g.LabelWidth + LabelGap + g.ValueWidth;
        dc.DrawText(value, Snap(new Point(valueRight - value.WidthIncludingTrailingWhitespace, y)));

        if (InlineSparks)
        {
            double sparkHeight = Math.Min(m.LineHeight * 0.62, 10 * m.Scale);
            var area = new Rect(valueRight + SparkGap, y + (m.LineHeight - sparkHeight) / 2 + 0.5, SparkWidth, sparkHeight);
            DrawSparkline(dc, cell, area, fillOpacity: 0.2, lineOpacity: 0.6, dot: true);
        }
    }

    // One series, no axes: faint area + thin line in the label (accent) color, the newest point emphasized.
    private void DrawSparkline(DrawingContext dc, MetricCell cell, Rect area, double fillOpacity, double lineOpacity, bool dot)
    {
        var history = cell.History;
        int take = Math.Min(history.Count, Math.Max(8, (int)(area.Width / (1.2 * _metrics!.Scale))));
        if (take < 2)
            return;
        double range = Math.Max(1e-9, cell.ScaleMax - cell.ScaleMin);
        double step = area.Width / (take - 1);
        var points = new List<Point>(take);
        for (int i = 0; i < take; i++)
        {
            double v = history[history.Count - take + i];
            if (!double.IsFinite(v))
                v = cell.ScaleMin;
            double t = Math.Clamp((v - cell.ScaleMin) / range, 0, 1);
            points.Add(new Point(area.Left + i * step, area.Bottom - t * area.Height));
        }

        var fill = new StreamGeometry();
        using (var ctx = fill.Open())
        {
            ctx.BeginFigure(new Point(points[0].X, area.Bottom), true, true);
            ctx.LineTo(points[0], false, false);
            ctx.PolyLineTo(points.Skip(1).ToList(), false, false);
            ctx.LineTo(new Point(points[^1].X, area.Bottom), false, false);
        }
        fill.Freeze();

        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(points[0], false, false);
            ctx.PolyLineTo(points.Skip(1).ToList(), true, true);
        }
        line.Freeze();

        dc.PushOpacity(fillOpacity);
        dc.DrawGeometry(_label, null, fill);
        dc.Pop();
        dc.PushOpacity(lineOpacity);
        dc.DrawGeometry(null, new Pen(_label, Math.Max(1, 1.1 * _metrics.Scale)) { LineJoin = PenLineJoin.Round }, line);
        dc.Pop();
        if (dot)
            dc.DrawEllipse(_label, null, points[^1], 1.4 * _metrics.Scale, 1.4 * _metrics.Scale);
    }

    private Point Snap(Point p)
    {
        double ppd = _metrics!.PixelsPerDip;
        return new Point(Math.Round(p.X * ppd) / ppd, Math.Round(p.Y * ppd) / ppd);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => Relayout();

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    private static Rect Inset(Rect r, double by) => new(r.X + by, r.Y + by, Math.Max(0, r.Width - 2 * by), Math.Max(0, r.Height - 2 * by));
}
