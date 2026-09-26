using TaskbarMonitor.Core;

namespace TaskbarMonitor.Overlay;

/// <summary>
/// Decides whether the foreground window is a fullscreen app (game, video, F11 browser) on the taskbar's monitor.
/// The shell window lists follow Kil0bit System Monitor (MIT).
/// </summary>
internal static class FullscreenDetector
{
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "MultitaskingViewFrame", "TaskView",
        "Windows.UI.Core.CoreWindow", "XamlExplorerViewHostWindow", "DesktopWindowXamlSource",
        "Windows.UI.Input.InputSite.WindowClass", "PopupHost", "TopLevelWindowForOverflowXamlIsland",
        "NotifyIconOverflowWindow", "ForegroundStaging", "ApplicationManager_ImmersiveShellWindow",
    };

    private static readonly HashSet<string> ShellProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "SearchApp", "dwm",
        "ShellHost", "LockApp", "TextInputHost",
    };

    public static bool IsShellWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return true;
        if (ShellClasses.Contains(Native.GetClassName(hwnd)))
            return true;
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0)
            return true;
        var name = Native.GetProcessName(pid);
        // Protected processes deny the query; those are system UI, not games.
        return name is null || ShellProcesses.Contains(name);
    }

    public static bool IsFullscreen(IntPtr hwnd, IntPtr taskbar)
    {
        if (hwnd == IntPtr.Zero || !Native.IsWindowVisible(hwnd) || Native.IsCloaked(hwnd))
            return false;
        // Maximized windows keep their caption; fullscreen ones drop it.
        if ((Native.GetStyle(hwnd) & Native.WS_CAPTION) == Native.WS_CAPTION)
            return false;
        if (!Native.GetWindowRect(hwnd, out var window) || !Native.TryGetMonitorRect(hwnd, out var monitor))
            return false;
        if (taskbar != IntPtr.Zero && Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST)
            != Native.MonitorFromWindow(taskbar, Native.MONITOR_DEFAULTTONEAREST))
            return false;
        bool covers = window.Left <= monitor.Left && window.Top <= monitor.Top && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;
        return covers && !IsShellWindow(hwnd);
    }
}
