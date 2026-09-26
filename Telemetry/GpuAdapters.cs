using System.Runtime.InteropServices;

namespace TaskbarMonitor.Telemetry;

public sealed record GpuAdapter(string Name, uint VendorId, uint DeviceId, uint SubSysId, long LuidLow, int LuidHigh, ulong DedicatedMemory)
{
    public const uint VendorNvidia = 0x10DE;
    public const uint VendorAmd = 0x1002;
    public const uint VendorIntel = 0x8086;

    // Stable id stored in settings; the same text PDH uses in its instance names ("luid_0x00000000_0x0000A6D4").
    public string Id => $"luid_0x{LuidHigh:X8}_0x{(uint)LuidLow:X8}";

    public override string ToString() => Name;
}

/// <summary>Enumerates hardware GPUs through DXGI: real names, LUIDs (to match PDH/D3DKMT) and PCI ids (to match NVML).</summary>
internal static unsafe class GpuAdapters
{
    private const int DXGI_ERROR_NOT_FOUND = unchecked((int)0x887A0002);
    private const uint DXGI_ADAPTER_FLAG_SOFTWARE = 2;
    private const uint VendorMicrosoft = 0x1414;

    [StructLayout(LayoutKind.Sequential)]
    private struct DxgiAdapterDesc1
    {
        public fixed char Description[128];
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public uint LuidLow;
        public int LuidHigh;
        public uint Flags;
    }

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(in Guid riid, out IntPtr factory);

    private static readonly Guid IID_IDXGIFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");

    public static IReadOnlyList<GpuAdapter> Enumerate()
    {
        var result = new List<GpuAdapter>();
        try
        {
            if (CreateDXGIFactory1(IID_IDXGIFactory1, out var factory) < 0 || factory == IntPtr.Zero)
                return result;
            try
            {
                // IDXGIFactory1 vtable: IUnknown(0-2), IDXGIObject(3-6), IDXGIFactory(7-11), EnumAdapters1 = 12.
                var enumAdapters1 = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)(*(void***)factory)[12];
                for (uint i = 0; ; i++)
                {
                    IntPtr adapter;
                    int hr = enumAdapters1(factory, i, &adapter);
                    if (hr == DXGI_ERROR_NOT_FOUND || hr < 0)
                        break;
                    try
                    {
                        // IDXGIAdapter1 vtable: IUnknown(0-2), IDXGIObject(3-6), IDXGIAdapter(7-9), GetDesc1 = 10.
                        var getDesc1 = (delegate* unmanaged[Stdcall]<IntPtr, DxgiAdapterDesc1*, int>)(*(void***)adapter)[10];
                        DxgiAdapterDesc1 desc;
                        if (getDesc1(adapter, &desc) < 0)
                            continue;
                        if ((desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) != 0 || desc.VendorId == VendorMicrosoft)
                            continue;
                        var name = new string(desc.Description).TrimEnd('\0').Trim();
                        uint luidLow = desc.LuidLow;
                        int luidHigh = desc.LuidHigh;
                        if (result.Any(r => r.LuidLow == luidLow && r.LuidHigh == luidHigh))
                            continue;
                        result.Add(new GpuAdapter(name, desc.VendorId, desc.DeviceId, desc.SubSysId,
                            desc.LuidLow, desc.LuidHigh, desc.DedicatedVideoMemory));
                    }
                    finally
                    {
                        Marshal.Release(adapter);
                    }
                }
            }
            finally
            {
                Marshal.Release(factory);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or SEHException)
        {
            Core.ErrorLog.Write(ex);
        }
        return result;
    }

    // Auto choice: the GPU with the most dedicated memory is the discrete one on hybrid laptops.
    public static GpuAdapter? Pick(IReadOnlyList<GpuAdapter> adapters, string? id) =>
        adapters.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? adapters.OrderByDescending(a => a.DedicatedMemory).FirstOrDefault();
}
