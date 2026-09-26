using System.Globalization;
using TaskbarMonitor.Core;
using TaskbarMonitor.Telemetry;

namespace TaskbarMonitor.Overlay;

public enum Severity { Normal, Caution, Critical }

/// <summary>One label/value line of the overlay.</summary>
public sealed record MetricCell(
    MetricKind Kind,
    string Label,
    string Value,
    string[] ReserveValues,
    Severity Severity,
    IReadOnlyList<double> History,
    double ScaleMin,
    double ScaleMax);

public sealed record MetricColumn(MetricCell Top, MetricCell? Bottom)
{
    public IEnumerable<MetricCell> Cells => Bottom is null ? [Top] : [Top, Bottom];
}

/// <summary>Turns a snapshot and the settings into text the overlay draws.</summary>
public static class MetricLayout
{
    private const double NetMinScale = 100 * 1024;

    private static readonly string[] PercentReserve = ["100%"];
    private static readonly string[] TempReserve = ["100°"];
    private static readonly string[] NetReserve = [$"999 KB/s", $"{99.9.ToString("0.0", CultureInfo.CurrentCulture)} MB/s", "999 MB/s"];

    public static IReadOnlyList<MetricColumn> Build(MetricsSnapshot snapshot, AppSettings settings)
    {
        var cells = settings.Metrics
            .Where(m => m.Enabled)
            .Select(m => Cell(m.Kind, snapshot, settings))
            .ToList();

        var columns = new List<MetricColumn>();
        if (settings.TwoRows)
        {
            for (int i = 0; i < cells.Count; i += 2)
                columns.Add(new MetricColumn(cells[i], i + 1 < cells.Count ? cells[i + 1] : null));
        }
        else
        {
            columns.AddRange(cells.Select(c => new MetricColumn(c, null)));
        }
        return columns;
    }

    public static MetricCell Cell(MetricKind kind, MetricsSnapshot snapshot, AppSettings settings)
    {
        double value = snapshot[kind];
        var history = snapshot.History(kind);
        bool compact = settings.LabelStyle == LabelStyle.Compact;

        return kind switch
        {
            MetricKind.GpuTemp => new MetricCell(kind, Label(kind, compact, settings), FormatTemp(value), TempReserve,
                Grade(value, settings.WarnTemp, settings.CritTemp, settings), history, 25, 100),
            MetricKind.NetUp or MetricKind.NetDown => new MetricCell(kind, Label(kind, compact, settings), FormatSpeed(value), NetReserve,
                Severity.Normal, history, 0, Math.Max(NetMinScale, history.Where(double.IsFinite).DefaultIfEmpty(0).Max())),
            _ => new MetricCell(kind, Label(kind, compact, settings), FormatPercent(value), PercentReserve,
                Grade(value, settings.WarnPercent, settings.CritPercent, settings), history, 0, 100),
        };
    }

    public static string Label(MetricKind kind, bool compact, AppSettings settings) => kind switch
    {
        MetricKind.Cpu => compact ? "C" : "CPU",
        MetricKind.Ram => compact ? "R" : "RAM",
        MetricKind.Gpu => compact ? "G" : "GPU",
        MetricKind.GpuTemp => compact ? "T" : "TMP",
        MetricKind.Vram => compact ? "V" : "VRM",
        MetricKind.NetUp => compact ? "↑" : "UP",
        MetricKind.NetDown => compact ? "↓" : "DN",
        MetricKind.DiskActivity => compact ? "D" : "DSK",
        MetricKind.DiskSpace => DriveLabel(settings.SpaceDrive, compact),
        _ => "?",
    };

    private static string DriveLabel(string drive, bool compact)
    {
        var letter = string.IsNullOrEmpty(drive) ? "C" : drive[..1].ToUpperInvariant();
        return compact ? letter : letter + ":";
    }

    private static Severity Grade(double value, int warn, int crit, AppSettings settings)
    {
        if (!settings.LoadColors || double.IsNaN(value))
            return Severity.Normal;
        if (value >= crit)
            return Severity.Critical;
        return value >= warn ? Severity.Caution : Severity.Normal;
    }

    public static string FormatPercent(double v) => double.IsNaN(v) ? "—" : $"{Math.Round(v):0}%";

    public static string FormatTemp(double v) => double.IsNaN(v) ? "—" : $"{Math.Round(v):0}°";

    public static string FormatSpeed(double bytesPerSecond)
    {
        if (double.IsNaN(bytesPerSecond))
            return "—";
        double kb = bytesPerSecond / 1024;
        if (kb < 999.5)
            return $"{kb:0} KB/s";
        double mb = kb / 1024;
        if (mb < 99.95)
            return mb.ToString("0.0", CultureInfo.CurrentCulture) + " MB/s";
        if (mb < 999.5)
            return $"{mb:0} MB/s";
        return (mb / 1024).ToString("0.0", CultureInfo.CurrentCulture) + " GB/s";
    }

    // Plausible values for previews before the first real sample arrives.
    public static MetricsSnapshot DemoSnapshot()
    {
        var rng = new Random(7);
        var values = new double[MetricsSnapshot.KindCount];
        var history = new double[MetricsSnapshot.KindCount][];
        double[] typical = [23, 55, 66, 48, 38, 42_000, 1_800_000, 9, 71];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = typical[i];
            history[i] = Enumerable.Range(0, 40).Select(t => Math.Max(0, typical[i] * (0.6 + 0.4 * Math.Sin(t / 4.0) + rng.NextDouble() * 0.3))).ToArray();
            history[i][^1] = typical[i];
        }
        return new MetricsSnapshot(values, history);
    }
}
