using System.Runtime.InteropServices;

namespace TaskbarMonitor.Telemetry;

/// <summary>
/// Minimal wrapper over pdh.dll. One query holds every counter so a single collect call samples them all;
/// wildcard counters "(*)" pick up instances that appear later (new processes using the GPU, etc.).
/// </summary>
internal sealed class PdhQuery : IDisposable
{
    private const uint PDH_FMT_DOUBLE = 0x00000200;
    private const uint PDH_FMT_NOCAP100 = 0x00008000;
    private const uint PDH_MORE_DATA = 0x800007D2;
    private const uint PDH_CSTATUS_VALID_DATA = 0x0;
    private const uint PDH_CSTATUS_NEW_DATA = 0x1;

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct FmtCounterValue
    {
        [FieldOffset(0)] public uint CStatus;
        [FieldOffset(8)] public double DoubleValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FmtCounterValueItem
    {
        public IntPtr Name;
        public FmtCounterValue Value;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQueryW(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out FmtCounterValue value);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr buffer);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);

    private IntPtr _query;
    private IntPtr _buffer;
    private uint _bufferSize;

    public PdhQuery()
    {
        if (PdhOpenQueryW(null, IntPtr.Zero, out _query) != 0)
            _query = IntPtr.Zero;
    }

    public bool IsOpen => _query != IntPtr.Zero;

    /// <returns>Counter handle, or <see cref="IntPtr.Zero"/> when the counter does not exist on this system.</returns>
    public IntPtr Add(string englishPath)
    {
        if (_query == IntPtr.Zero)
            return IntPtr.Zero;
        return PdhAddEnglishCounterW(_query, englishPath, IntPtr.Zero, out var counter) == 0 ? counter : IntPtr.Zero;
    }

    public bool Collect() => _query != IntPtr.Zero && PdhCollectQueryData(_query) == 0;

    public double Read(IntPtr counter)
    {
        if (counter == IntPtr.Zero)
            return double.NaN;
        uint status = PdhGetFormattedCounterValue(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, out _, out var value);
        return status == 0 && value.CStatus is PDH_CSTATUS_VALID_DATA or PDH_CSTATUS_NEW_DATA ? value.DoubleValue : double.NaN;
    }

    /// <summary>Calls <paramref name="visit"/> for every instance of a wildcard counter.</summary>
    public bool ReadArray(IntPtr counter, Action<string, double> visit)
    {
        if (counter == IntPtr.Zero)
            return false;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            uint size = _bufferSize;
            uint status = PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out uint count, _buffer);
            if (status == PDH_MORE_DATA)
            {
                // Instances can appear between calls; leave some headroom.
                Grow(size + size / 4 + 1024);
                continue;
            }
            if (status != 0)
                return false;

            int stride = Marshal.SizeOf<FmtCounterValueItem>();
            for (int i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<FmtCounterValueItem>(_buffer + i * stride);
                if (item.Value.CStatus is not (PDH_CSTATUS_VALID_DATA or PDH_CSTATUS_NEW_DATA))
                    continue;
                var name = Marshal.PtrToStringUni(item.Name);
                if (name is not null)
                    visit(name, item.Value.DoubleValue);
            }
            return true;
        }
        return false;
    }

    private void Grow(uint size)
    {
        if (_buffer != IntPtr.Zero)
            Marshal.FreeHGlobal(_buffer);
        _buffer = Marshal.AllocHGlobal((int)size);
        _bufferSize = size;
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero)
        {
            PdhCloseQuery(_query);
            _query = IntPtr.Zero;
        }
        if (_buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_buffer);
            _buffer = IntPtr.Zero;
            _bufferSize = 0;
        }
    }
}
