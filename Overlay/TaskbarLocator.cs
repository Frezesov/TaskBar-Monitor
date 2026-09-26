using TaskbarMonitor.Core;

namespace TaskbarMonitor.Overlay;

/// <summary>Finds the primary taskbar and its notification area (tray) in physical pixels.</summary>
internal sealed class TaskbarLocator
{
    public IntPtr Taskbar { get; private set; }
    public IntPtr Tray { get; private set; }
    public uint ExplorerProcessId { get; private set; }

    public bool IsValid => Taskbar != IntPtr.Zero && Native.IsWindow(Taskbar);

    public bool Refresh()
    {
        Taskbar = Native.FindWindow("Shell_TrayWnd", null);
        Tray = Taskbar == IntPtr.Zero ? IntPtr.Zero : Native.FindWindowEx(Taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        ExplorerProcessId = 0;
        if (Taskbar != IntPtr.Zero)
        {
            Native.GetWindowThreadProcessId(Taskbar, out uint pid);
            ExplorerProcessId = pid;
        }
        return Taskbar != IntPtr.Zero;
    }

    public bool TryGetTaskbarRect(out Native.RECT rect)
    {
        rect = default;
        return IsValid && Native.GetWindowRect(Taskbar, out rect) && !rect.IsEmpty;
    }

    /// <summary>Left edge of the tray, or the taskbar's right edge when the tray cannot be found.</summary>
    public int TrayLeft(Native.RECT taskbar) =>
        Tray != IntPtr.Zero && Native.GetWindowRect(Tray, out var tray) && !tray.IsEmpty && tray.Left > taskbar.Left
            ? tray.Left
            : taskbar.Right;

    public static bool IsHorizontal(Native.RECT taskbar) => taskbar.Width >= taskbar.Height;

    /// <summary>False while an auto-hiding taskbar is slid off its monitor.</summary>
    public bool IsOnScreen(Native.RECT taskbar)
    {
        if (!Native.TryGetMonitorRect(Taskbar, out var monitor))
            return true;
        int visibleTop = Math.Max(taskbar.Top, monitor.Top);
        int visibleBottom = Math.Min(taskbar.Bottom, monitor.Bottom);
        int visibleLeft = Math.Max(taskbar.Left, monitor.Left);
        int visibleRight = Math.Min(taskbar.Right, monitor.Right);
        int visible = IsHorizontal(taskbar) ? visibleBottom - visibleTop : visibleRight - visibleLeft;
        int thickness = IsHorizontal(taskbar) ? taskbar.Height : taskbar.Width;
        return visible * 2 >= thickness;
    }

    public double Dpi()
    {
        uint dpi = IsValid ? Native.GetDpiForWindow(Taskbar) : 0;
        return dpi == 0 ? 1.0 : dpi / 96.0;
    }
}
