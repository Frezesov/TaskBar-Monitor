using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace TaskbarMonitor.Core;

/// <summary>One system-wide shortcut, registered with RegisterHotKey on a hidden message-only window.</summary>
internal sealed class GlobalHotkey : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 0x7B01;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly HwndSource _source;
    private bool _registered;

    public GlobalHotkey()
    {
        _source = new HwndSource(new HwndSourceParameters("TaskbarMonitor.Hotkey") { ParentWindow = HWND_MESSAGE, WindowStyle = 0 });
        _source.AddHook(Hook);
    }

    public event Action? Pressed;

    /// <summary>Replaces the registered shortcut; false when another program already owns the combination.</summary>
    public bool Register(HotkeyGesture gesture)
    {
        Unregister();
        if (!gesture.IsValid)
            return false;
        uint modifiers = MOD_NOREPEAT;
        if (gesture.Modifiers.HasFlag(ModifierKeys.Control))
            modifiers |= MOD_CONTROL;
        if (gesture.Modifiers.HasFlag(ModifierKeys.Alt))
            modifiers |= MOD_ALT;
        if (gesture.Modifiers.HasFlag(ModifierKeys.Shift))
            modifiers |= MOD_SHIFT;
        if (gesture.Modifiers.HasFlag(ModifierKeys.Windows))
            modifiers |= MOD_WIN;
        _registered = RegisterHotKey(_source.Handle, HotkeyId, modifiers, (uint)KeyInterop.VirtualKeyFromKey(gesture.Key));
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered)
            return;
        UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = false;
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(Hook);
        _source.Dispose();
    }
}
