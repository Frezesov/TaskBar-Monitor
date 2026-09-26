using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using TaskbarMonitor.Core;

namespace TaskbarMonitor.Theming;

/// <summary>
/// Listens for light/dark, accent and high-contrast changes on a hidden top-level window
/// (broadcasts never reach message-only windows) and re-reads the theme when they happen.
/// </summary>
internal sealed class ThemeWatcher : IDisposable
{
    private readonly HwndSource _source;
    private readonly DispatcherTimer _quick;
    private readonly DispatcherTimer _late;

    public ThemeWatcher()
    {
        Current = TaskbarTheme.Read();

        _source = new HwndSource(new HwndSourceParameters("TaskbarMonitor.ThemeWatcher")
        {
            WindowStyle = 0,
            ExtendedWindowStyle = (int)Native.WS_EX_TOOLWINDOW,
            Width = 0,
            Height = 0,
        });
        _source.AddHook(Hook);

        // Windows writes the new values shortly after broadcasting: read once quickly and once more a bit later.
        _quick = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background, OnTick, Dispatcher.CurrentDispatcher);
        _late = new DispatcherTimer(TimeSpan.FromMilliseconds(700), DispatcherPriority.Background, OnTick, Dispatcher.CurrentDispatcher);
        _quick.Stop();
        _late.Stop();
    }

    public TaskbarTheme Current { get; private set; }

    public event Action? Changed;

    public void Refresh()
    {
        var next = TaskbarTheme.Read();
        if (next == Current)
            return;
        Current = next;
        Changed?.Invoke();
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case Native.WM_SETTINGCHANGE:
                if (wParam == Native.SPI_SETHIGHCONTRAST || IsImmersiveColorSet(lParam))
                    Schedule();
                break;
            case Native.WM_DWMCOLORIZATIONCOLORCHANGED:
            case Native.WM_SYSCOLORCHANGE:
            case Native.WM_THEMECHANGED:
                Schedule();
                break;
        }
        return IntPtr.Zero;
    }

    private static bool IsImmersiveColorSet(IntPtr lParam) =>
        lParam != IntPtr.Zero && string.Equals(Marshal.PtrToStringUni(lParam), "ImmersiveColorSet", StringComparison.Ordinal);

    private void Schedule()
    {
        _quick.Stop();
        _quick.Start();
        _late.Stop();
        _late.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        ((DispatcherTimer)sender!).Stop();
        Refresh();
    }

    public void Dispose()
    {
        _quick.Stop();
        _late.Stop();
        _source.RemoveHook(Hook);
        _source.Dispose();
    }
}
