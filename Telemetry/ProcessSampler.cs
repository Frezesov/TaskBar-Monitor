using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TaskbarMonitor.Telemetry;

/// <summary>CPU share of all processes with the same image name, in percent of the whole CPU.</summary>
public sealed record ProcessLoad(string Name, double CpuPercent, int Count);

/// <summary>
/// Per-process CPU use from a single NtQuerySystemInformation call. Unlike Process.GetProcesses it opens
/// no process handles, so it is cheap and sees protected and elevated processes too.
/// </summary>
internal sealed unsafe class ProcessSampler : IDisposable
{
    private const int SystemProcessInformation = 5;
    private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);
    private const int MaxBufferSize = 64 * 1024 * 1024;

    // SYSTEM_PROCESS_INFORMATION field offsets on x64.
    private const int NextEntryOffset = 0x00;
    private const int CreateTimeOffset = 0x20;
    private const int UserTimeOffset = 0x28;
    private const int KernelTimeOffset = 0x30;
    private const int ImageNameLengthOffset = 0x38;
    private const int ImageNameBufferOffset = 0x40;
    private const int ProcessIdOffset = 0x50;

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int infoClass, void* buffer, int length, out int returnLength);

    private Dictionary<(nint Pid, long Created), long> _previous = [];
    private Dictionary<(nint Pid, long Created), long> _current = [];
    private readonly Dictionary<string, (double Cpu, int Count)> _byName = new(StringComparer.OrdinalIgnoreCase);
    private long _previousStamp;
    private void* _buffer;
    private int _size = 512 * 1024;

    /// <summary>The busiest processes since the previous call; null on the first call, which only takes a baseline.</summary>
    public IReadOnlyList<ProcessLoad>? Sample(int count)
    {
        if (IntPtr.Size != 8 || !Query())
            return null;

        long stamp = Stopwatch.GetTimestamp();
        double capacity = _previousStamp == 0
            ? 0
            : (stamp - _previousStamp) * 10_000_000.0 / Stopwatch.Frequency * Environment.ProcessorCount;
        _current.Clear();
        _byName.Clear();

        byte* entry = (byte*)_buffer;
        while (true)
        {
            nint pid = *(nint*)(entry + ProcessIdOffset);
            // PID 0 is the idle process: its time is the CPU's free time.
            if (pid != 0)
            {
                var key = (pid, *(long*)(entry + CreateTimeOffset));
                long time = *(long*)(entry + UserTimeOffset) + *(long*)(entry + KernelTimeOffset);
                _current[key] = time;
                if (capacity > 0 && _previous.TryGetValue(key, out long before) && time > before)
                {
                    string name = ImageName(entry);
                    var (cpu, n) = _byName.GetValueOrDefault(name);
                    _byName[name] = (cpu + 100.0 * (time - before) / capacity, n + 1);
                }
            }
            uint next = *(uint*)(entry + NextEntryOffset);
            if (next == 0)
                break;
            entry += next;
        }

        (_previous, _current) = (_current, _previous);
        _previousStamp = stamp;
        if (capacity <= 0)
            return null;
        return _byName
            .OrderByDescending(p => p.Value.Cpu)
            .Take(count)
            .Select(p => new ProcessLoad(p.Key, Math.Min(100, p.Value.Cpu), p.Value.Count))
            .ToList();
    }

    private static string ImageName(byte* entry)
    {
        ushort bytes = *(ushort*)(entry + ImageNameLengthOffset);
        char* text = *(char**)(entry + ImageNameBufferOffset);
        if (bytes == 0 || text == null)
            return "System";
        var name = new string(text, 0, bytes / 2);
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    private bool Query()
    {
        while (true)
        {
            _buffer = _buffer == null ? NativeMemory.Alloc((nuint)_size) : _buffer;
            int status = NtQuerySystemInformation(SystemProcessInformation, _buffer, _size, out int needed);
            if (status != StatusInfoLengthMismatch)
                return status >= 0;
            FreeBuffer();
            // Processes may start between the calls: leave some room.
            _size = Math.Max(needed, _size) + 64 * 1024;
            if (_size > MaxBufferSize)
                return false;
        }
    }

    /// <summary>Drops the baseline and the buffer while nobody looks at the numbers.</summary>
    public void Reset()
    {
        _previous.Clear();
        _current.Clear();
        _previousStamp = 0;
        FreeBuffer();
    }

    private void FreeBuffer()
    {
        if (_buffer == null)
            return;
        NativeMemory.Free(_buffer);
        _buffer = null;
    }

    public void Dispose() => Reset();
}
