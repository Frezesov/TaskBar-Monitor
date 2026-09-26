using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace TaskbarMonitor.Telemetry;

/// <summary>
/// NVIDIA Management Library, shipped with every NVIDIA driver (System32\nvml.dll).
/// Same data as nvidia-smi without keeping a child process alive.
/// </summary>
internal sealed unsafe class Nvml : IDisposable
{
    private const int NVML_SUCCESS = 0;
    private const int NVML_TEMPERATURE_GPU = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct UtilizationRates { public uint Gpu; public uint Memory; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Memory { public ulong Total; public ulong Free; public ulong Used; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PciInfo
    {
        public fixed byte BusIdLegacy[16];
        public uint Domain;
        public uint Bus;
        public uint Device;
        public uint PciDeviceId;
        public uint PciSubSystemId;
        public fixed byte BusId[32];
    }

    private readonly IntPtr _library;
    private readonly delegate* unmanaged[Cdecl]<int> _shutdown;
    private readonly delegate* unmanaged[Cdecl]<IntPtr, UtilizationRates*, int> _getUtilization;
    private readonly delegate* unmanaged[Cdecl]<IntPtr, int, uint*, int> _getTemperature;
    private readonly delegate* unmanaged[Cdecl]<IntPtr, Memory*, int> _getMemory;
    private readonly List<(IntPtr Handle, string Name, uint PciDeviceId, uint PciSubSystemId)> _devices = [];

    private Nvml(IntPtr library)
    {
        _library = library;
        var init = (delegate* unmanaged[Cdecl]<int>)NativeLibrary.GetExport(library, "nvmlInit_v2");
        _shutdown = (delegate* unmanaged[Cdecl]<int>)NativeLibrary.GetExport(library, "nvmlShutdown");
        var getCount = (delegate* unmanaged[Cdecl]<uint*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetCount_v2");
        var getHandle = (delegate* unmanaged[Cdecl]<uint, IntPtr*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetHandleByIndex_v2");
        var getName = (delegate* unmanaged[Cdecl]<IntPtr, byte*, uint, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetName");
        var getPci = (delegate* unmanaged[Cdecl]<IntPtr, PciInfo*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetPciInfo_v3");
        _getUtilization = (delegate* unmanaged[Cdecl]<IntPtr, UtilizationRates*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetUtilizationRates");
        _getTemperature = (delegate* unmanaged[Cdecl]<IntPtr, int, uint*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetTemperature");
        _getMemory = (delegate* unmanaged[Cdecl]<IntPtr, Memory*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetMemoryInfo");

        if (init() != NVML_SUCCESS)
            throw new InvalidOperationException("nvmlInit failed");

        uint count;
        if (getCount(&count) != NVML_SUCCESS)
            count = 0;
        byte* name = stackalloc byte[96];
        for (uint i = 0; i < count; i++)
        {
            IntPtr handle;
            if (getHandle(i, &handle) != NVML_SUCCESS)
                continue;
            string text = getName(handle, name, 96) == NVML_SUCCESS ? Encoding.UTF8.GetString(name, IndexOfZero(name, 96)) : "";
            PciInfo pci;
            bool hasPci = getPci(handle, &pci) == NVML_SUCCESS;
            _devices.Add((handle, text, hasPci ? pci.PciDeviceId : 0, hasPci ? pci.PciSubSystemId : 0));
        }
    }

    public static Nvml? TryLoad()
    {
        foreach (var path in Candidates())
        {
            if (!NativeLibrary.TryLoad(path, out var library))
                continue;
            try
            {
                return new Nvml(library);
            }
            catch (Exception ex) when (ex is EntryPointNotFoundException or InvalidOperationException)
            {
                NativeLibrary.Free(library);
            }
        }
        return null;
    }

    private static IEnumerable<string> Candidates()
    {
        yield return Path.Combine(Environment.SystemDirectory, "nvml.dll");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvml.dll");
    }

    /// <summary>Finds the NVML device behind a DXGI adapter by PCI ids, falling back to the name.</summary>
    public IntPtr Find(GpuAdapter adapter)
    {
        uint pciDevice = (adapter.DeviceId << 16) | adapter.VendorId;
        foreach (var d in _devices)
            if (d.PciDeviceId == pciDevice && (adapter.SubSysId == 0 || d.PciSubSystemId == adapter.SubSysId))
                return d.Handle;
        foreach (var d in _devices)
            if (string.Equals(d.Name, adapter.Name, StringComparison.OrdinalIgnoreCase))
                return d.Handle;
        return IntPtr.Zero;
    }

    public double Utilization(IntPtr device)
    {
        UtilizationRates u;
        return _getUtilization(device, &u) == NVML_SUCCESS ? u.Gpu : double.NaN;
    }

    public double Temperature(IntPtr device)
    {
        uint t;
        return _getTemperature(device, NVML_TEMPERATURE_GPU, &t) == NVML_SUCCESS ? t : double.NaN;
    }

    public (double Used, double Total) MemoryInfo(IntPtr device)
    {
        Memory m;
        return _getMemory(device, &m) == NVML_SUCCESS ? (m.Used, m.Total) : (double.NaN, double.NaN);
    }

    private static int IndexOfZero(byte* p, int max)
    {
        for (int i = 0; i < max; i++)
            if (p[i] == 0)
                return i;
        return max;
    }

    public void Dispose()
    {
        _shutdown();
        NativeLibrary.Free(_library);
    }
}
