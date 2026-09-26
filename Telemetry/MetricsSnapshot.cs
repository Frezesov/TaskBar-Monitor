using TaskbarMonitor.Core;

namespace TaskbarMonitor.Telemetry;

/// <summary>One immutable sample of every metric plus the recent history used by sparklines. NaN = unavailable.</summary>
public sealed class MetricsSnapshot
{
    public static readonly int KindCount = Enum.GetValues<MetricKind>().Length;

    private readonly double[] _values;
    private readonly double[][] _history;

    public MetricsSnapshot(double[] values, double[][] history)
    {
        _values = values;
        _history = history;
    }

    public double this[MetricKind kind] => _values[(int)kind];

    /// <summary>Oldest → newest.</summary>
    public IReadOnlyList<double> History(MetricKind kind) => _history[(int)kind];

    public double RamUsedGb { get; init; } = double.NaN;
    public double RamTotalGb { get; init; } = double.NaN;
    public string GpuName { get; init; } = "";
    public string GpuSource { get; init; } = "";

    public static MetricsSnapshot Empty { get; } = new(
        Enumerable.Repeat(double.NaN, KindCount).ToArray(),
        Enumerable.Range(0, KindCount).Select(_ => Array.Empty<double>()).ToArray());
}

/// <summary>Fixed-size ring buffer of recent values for one metric.</summary>
internal sealed class History(int capacity)
{
    private readonly double[] _items = new double[capacity];
    private int _start;
    private int _count;

    public void Add(double value)
    {
        if (_count < _items.Length)
        {
            _items[(_start + _count) % _items.Length] = value;
            _count++;
        }
        else
        {
            _items[_start] = value;
            _start = (_start + 1) % _items.Length;
        }
    }

    public void Clear()
    {
        _start = 0;
        _count = 0;
    }

    public double[] ToArray()
    {
        var result = new double[_count];
        for (int i = 0; i < _count; i++)
            result[i] = _items[(_start + i) % _items.Length];
        return result;
    }
}
