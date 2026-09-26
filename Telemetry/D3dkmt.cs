using System.Runtime.InteropServices;

namespace TaskbarMonitor.Telemetry;

/// <summary>
/// GPU temperature from the WDDM kernel thunk — the source Task Manager uses. Works without admin rights
/// for drivers that report it (WDDM 2.5+, mostly discrete AMD/Intel/NVIDIA cards).
/// Layouts follow d3dkmthk.h (KMTQAITYPE_ADAPTERPERFDATA = 62).
/// </summary>
internal sealed unsafe class D3dkmtAdapter : IDisposable
{
    private const int KMTQAITYPE_ADAPTERPERFDATA = 62;

    [StructLayout(LayoutKind.Sequential)]
    private struct OpenAdapterFromLuid
    {
        public uint LuidLow;
        public int LuidHigh;
        public uint Adapter;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CloseAdapter
    {
        public uint Adapter;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct QueryAdapterInfo
    {
        public uint Adapter;
        public int Type;
        public void* PrivateDriverData;
        public uint PrivateDriverDataSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdapterPerfData
    {
        public uint PhysicalAdapterIndex;
        public ulong MemoryFrequency;
        public ulong MaxMemoryFrequency;
        public ulong MaxMemoryFrequencyOC;
        public ulong MemoryBandwidth;
        public ulong PCIEBandwidth;
        public uint FanRPM;
        public uint Power;
        public uint Temperature; // deci-Celsius
        public byte PowerStateOverride;
    }

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTOpenAdapterFromLuid(ref OpenAdapterFromLuid data);

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTCloseAdapter(ref CloseAdapter data);

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTQueryAdapterInfo(ref QueryAdapterInfo data);

    private uint _handle;

    private D3dkmtAdapter(uint handle) => _handle = handle;

    public static D3dkmtAdapter? Open(GpuAdapter adapter)
    {
        try
        {
            var open = new OpenAdapterFromLuid { LuidLow = (uint)adapter.LuidLow, LuidHigh = adapter.LuidHigh };
            return D3DKMTOpenAdapterFromLuid(ref open) == 0 ? new D3dkmtAdapter(open.Adapter) : null;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return null;
        }
    }

    public double Temperature()
    {
        if (_handle == 0)
            return double.NaN;
        var perf = new AdapterPerfData();
        var query = new QueryAdapterInfo
        {
            Adapter = _handle,
            Type = KMTQAITYPE_ADAPTERPERFDATA,
            PrivateDriverData = &perf,
            PrivateDriverDataSize = (uint)sizeof(AdapterPerfData),
        };
        if (D3DKMTQueryAdapterInfo(ref query) != 0 || perf.Temperature == 0)
            return double.NaN;
        double celsius = perf.Temperature / 10.0;
        return celsius is > 0 and < 150 ? celsius : double.NaN;
    }

    public void Dispose()
    {
        if (_handle == 0)
            return;
        var close = new CloseAdapter { Adapter = _handle };
        D3DKMTCloseAdapter(ref close);
        _handle = 0;
    }
}
