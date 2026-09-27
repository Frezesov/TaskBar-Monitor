using System.IO;
using System.Runtime.InteropServices;
using TaskbarMonitor.Core;

namespace TaskbarMonitor.Telemetry;

public sealed record TelemetryOptions(IReadOnlySet<MetricKind> Enabled, string GpuId, string NetworkAdapter, string SpaceDrive, int IntervalMs);

/// <summary>
/// Samples all metrics on a background loop and publishes immutable snapshots.
/// GPU load follows Task Manager: utilization is summed per engine across processes, the busiest engine wins.
/// </summary>
internal sealed class TelemetryService : IDisposable
{
    public const int HistoryLength = 60;

    private const double Gb = 1073741824.0;

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    private readonly CancellationTokenSource _cts = new();
    private readonly History[] _history = Enumerable.Range(0, MetricsSnapshot.KindCount).Select(_ => new History(HistoryLength)).ToArray();
    private readonly NetworkSampler _network = new();
    private readonly ProcessSampler _processes = new();
    private readonly Dictionary<string, double> _engineSums = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    private TelemetryOptions _options;
    private bool _reconfigure = true;
    private Task? _loop;

    private PdhQuery? _pdh;
    private IntPtr _cpuCounter;
    private IntPtr _gpuEngineCounter;
    private IntPtr _gpuMemoryCounter;
    private IntPtr _diskIdleCounter;
    private GpuAdapter? _gpu;
    private Nvml? _nvml;
    private bool _nvmlTried;
    private IntPtr _nvmlDevice;
    private D3dkmtAdapter? _d3dkmt;
    private volatile bool _sampleProcesses;

    public TelemetryService(TelemetryOptions options) => _options = options;

    public event Action<MetricsSnapshot>? Updated;

    public MetricsSnapshot Latest { get; private set; } = MetricsSnapshot.Empty;

    public void Start() => _loop = Task.Run(() => RunAsync(_cts.Token));

    public void Configure(TelemetryOptions options)
    {
        lock (_gate)
        {
            _options = options;
            _reconfigure = true;
        }
    }

    /// <summary>Turns the per-process CPU list on while something shows it; it costs a system-wide query per tick.</summary>
    public void SetProcessSampling(bool on) => _sampleProcesses = on;

    private async Task RunAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_options.IntervalMs));
        do
        {
            try
            {
                TelemetryOptions options;
                bool reconfigure;
                lock (_gate)
                {
                    options = _options;
                    reconfigure = _reconfigure;
                    _reconfigure = false;
                }
                if (reconfigure)
                {
                    Rebuild(options);
                    timer.Period = TimeSpan.FromMilliseconds(options.IntervalMs);
                }
                var snapshot = Sample(options);
                Latest = snapshot;
                Updated?.Invoke(snapshot);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ErrorLog.Write(ex);
            }
        }
        while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false));
    }

    private void Rebuild(TelemetryOptions options)
    {
        _pdh?.Dispose();
        _d3dkmt?.Dispose();
        _d3dkmt = null;

        var on = options.Enabled;
        bool wantGpu = on.Contains(MetricKind.Gpu) || on.Contains(MetricKind.GpuTemp) || on.Contains(MetricKind.Vram);
        _gpu = wantGpu ? GpuAdapters.Pick(GpuAdapters.Enumerate(), options.GpuId) : null;
        _nvmlDevice = IntPtr.Zero;
        // TASKBARMONITOR_NO_NVML=1 forces the vendor-neutral path (PDH + D3DKMT), for troubleshooting.
        if (_gpu is { VendorId: GpuAdapter.VendorNvidia } && Environment.GetEnvironmentVariable("TASKBARMONITOR_NO_NVML") != "1")
        {
            if (!_nvmlTried)
            {
                _nvmlTried = true;
                _nvml = Nvml.TryLoad();
            }
            if (_nvml is not null)
                _nvmlDevice = _nvml.Find(_gpu);
        }
        if (_gpu is not null && _nvmlDevice == IntPtr.Zero)
            _d3dkmt = D3dkmtAdapter.Open(_gpu);

        _pdh = new PdhQuery();
        _cpuCounter = on.Contains(MetricKind.Cpu)
            ? FirstCounter(@"\Processor Information(_Total)\% Processor Utility", @"\Processor(_Total)\% Processor Time")
            : IntPtr.Zero;
        _gpuEngineCounter = on.Contains(MetricKind.Gpu) && _nvmlDevice == IntPtr.Zero
            ? _pdh.Add(@"\GPU Engine(*)\Utilization Percentage")
            : IntPtr.Zero;
        _gpuMemoryCounter = on.Contains(MetricKind.Vram) && _nvmlDevice == IntPtr.Zero
            ? _pdh.Add(@"\GPU Adapter Memory(*)\Dedicated Usage")
            : IntPtr.Zero;
        _diskIdleCounter = on.Contains(MetricKind.DiskActivity)
            ? _pdh.Add(@"\PhysicalDisk(*)\% Idle Time")
            : IntPtr.Zero;
        // Rate counters need two samples: prime the first one now.
        _pdh.Collect();

        foreach (var h in _history)
            h.Clear();
    }

    private IntPtr FirstCounter(params string[] paths)
    {
        foreach (var path in paths)
        {
            var counter = _pdh!.Add(path);
            if (counter != IntPtr.Zero)
                return counter;
        }
        return IntPtr.Zero;
    }

    private MetricsSnapshot Sample(TelemetryOptions options)
    {
        var on = options.Enabled;
        var values = Enumerable.Repeat(double.NaN, MetricsSnapshot.KindCount).ToArray();
        _pdh?.Collect();

        if (on.Contains(MetricKind.Cpu) && _pdh is not null)
            values[(int)MetricKind.Cpu] = Clamp(_pdh.Read(_cpuCounter));

        double ramUsed = double.NaN, ramTotal = double.NaN;
        var memory = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (GlobalMemoryStatusEx(ref memory) && memory.TotalPhys > 0)
        {
            ramTotal = memory.TotalPhys / 1073741824.0;
            ramUsed = (memory.TotalPhys - memory.AvailPhys) / 1073741824.0;
            if (on.Contains(MetricKind.Ram))
                values[(int)MetricKind.Ram] = Clamp(100.0 * (memory.TotalPhys - memory.AvailPhys) / memory.TotalPhys);
        }

        string gpuSource = "";
        double vramUsed = double.NaN, vramTotal = double.NaN;
        if (_gpu is not null)
        {
            if (on.Contains(MetricKind.Gpu))
                values[(int)MetricKind.Gpu] = _nvmlDevice != IntPtr.Zero ? Clamp(_nvml!.Utilization(_nvmlDevice)) : GpuLoadFromPdh(_gpu);
            if (on.Contains(MetricKind.GpuTemp))
                values[(int)MetricKind.GpuTemp] = _nvmlDevice != IntPtr.Zero ? _nvml!.Temperature(_nvmlDevice) : _d3dkmt?.Temperature() ?? double.NaN;
            if (on.Contains(MetricKind.Vram))
                (values[(int)MetricKind.Vram], vramUsed, vramTotal) = Vram(_gpu);
            gpuSource = _nvmlDevice != IntPtr.Zero ? "NVML" : "PDH / D3DKMT";
        }

        if (on.Contains(MetricKind.NetUp) || on.Contains(MetricKind.NetDown))
        {
            var (up, down) = _network.Sample(options.NetworkAdapter);
            if (on.Contains(MetricKind.NetUp))
                values[(int)MetricKind.NetUp] = up;
            if (on.Contains(MetricKind.NetDown))
                values[(int)MetricKind.NetDown] = down;
        }

        if (on.Contains(MetricKind.DiskActivity) && _pdh is not null)
        {
            double busiest = double.NaN;
            _pdh.ReadArray(_diskIdleCounter, (name, idle) =>
            {
                if (name == "_Total")
                    return;
                double active = 100 - Math.Clamp(idle, 0, 100);
                busiest = double.IsNaN(busiest) ? active : Math.Max(busiest, active);
            });
            values[(int)MetricKind.DiskActivity] = busiest;
        }

        double diskFree = double.NaN, diskTotal = double.NaN;
        if (on.Contains(MetricKind.DiskSpace))
            (values[(int)MetricKind.DiskSpace], diskFree, diskTotal) = DiskSpace(options.SpaceDrive);

        IReadOnlyList<ProcessLoad>? top = null;
        if (_sampleProcesses)
            top = _processes.Sample(3);
        else
            _processes.Reset();

        var history = new double[MetricsSnapshot.KindCount][];
        for (int i = 0; i < values.Length; i++)
        {
            _history[i].Add(values[i]);
            history[i] = _history[i].ToArray();
        }

        return new MetricsSnapshot(values, history)
        {
            RamUsedGb = ramUsed,
            RamTotalGb = ramTotal,
            VramUsedGb = vramUsed,
            VramTotalGb = vramTotal,
            DiskFreeGb = diskFree,
            DiskTotalGb = diskTotal,
            TopProcesses = top,
            GpuName = _gpu?.Name ?? "",
            GpuSource = gpuSource,
        };
    }

    // Instance: "pid_1234_luid_0x00000000_0x0000A6D4_phys_0_eng_3_engtype_VideoDecode".
    private double GpuLoadFromPdh(GpuAdapter gpu)
    {
        if (_pdh is null || _gpuEngineCounter == IntPtr.Zero)
            return double.NaN;
        _engineSums.Clear();
        string id = gpu.Id;
        bool ok = _pdh.ReadArray(_gpuEngineCounter, (name, value) =>
        {
            int luid = name.IndexOf(id, StringComparison.OrdinalIgnoreCase);
            if (luid < 0)
                return;
            int engtype = name.IndexOf("_engtype_", luid, StringComparison.Ordinal);
            string engine = engtype > 0 ? name[(luid + id.Length)..engtype] : name[(luid + id.Length)..];
            _engineSums[engine] = _engineSums.GetValueOrDefault(engine) + value;
        });
        if (!ok)
            return double.NaN;
        return _engineSums.Count == 0 ? 0 : Clamp(_engineSums.Values.Max());
    }

    private (double Percent, double UsedGb, double TotalGb) Vram(GpuAdapter gpu)
    {
        double used, total;
        if (_nvmlDevice != IntPtr.Zero)
        {
            (used, total) = _nvml!.MemoryInfo(_nvmlDevice);
        }
        else
        {
            if (_pdh is null || _gpuMemoryCounter == IntPtr.Zero || gpu.DedicatedMemory == 0)
                return (double.NaN, double.NaN, double.NaN);
            double sum = double.NaN;
            _pdh.ReadArray(_gpuMemoryCounter, (name, value) =>
            {
                if (name.Contains(gpu.Id, StringComparison.OrdinalIgnoreCase))
                    sum = double.IsNaN(sum) ? value : sum + value;
            });
            (used, total) = (sum, gpu.DedicatedMemory);
        }
        return double.IsNaN(used) || !(total > 0)
            ? (double.NaN, double.NaN, double.NaN)
            : (Clamp(100 * used / total), used / Gb, total / Gb);
    }

    private static (double Percent, double FreeGb, double TotalGb) DiskSpace(string drive)
    {
        try
        {
            var info = new DriveInfo(string.IsNullOrWhiteSpace(drive) ? "C:\\" : drive);
            return info.IsReady && info.TotalSize > 0
                ? (100.0 * (info.TotalSize - info.TotalFreeSpace) / info.TotalSize, info.TotalFreeSpace / Gb, info.TotalSize / Gb)
                : (double.NaN, double.NaN, double.NaN);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return (double.NaN, double.NaN, double.NaN);
        }
    }

    private static double Clamp(double value) => double.IsNaN(value) ? value : Math.Clamp(value, 0, 100);

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }
        _pdh?.Dispose();
        _d3dkmt?.Dispose();
        _nvml?.Dispose();
        _network.Dispose();
        _processes.Dispose();
        _cts.Dispose();
    }
}
