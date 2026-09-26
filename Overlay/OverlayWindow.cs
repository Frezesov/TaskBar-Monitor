using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TaskbarMonitor.Core;
using TaskbarMonitor.ViewModels;

namespace TaskbarMonitor.Overlay;

/// <summary>
/// Transparent, non-activating strip drawn over the taskbar. While snapped it is owned by Shell_TrayWnd,
/// so Windows keeps it above the taskbar even after the taskbar is clicked.
/// </summary>
internal sealed class OverlayWindow : Window
{
    private static readonly Duration FadeDuration = new(TimeSpan.FromMilliseconds(150));

    private readonly SettingsViewModel _vm;
    private readonly OverlayView _view = new();
    private readonly TaskbarLocator _taskbar = new();
    private readonly OverlayMenu _menu;
    private readonly DispatcherTimer _repositionTimer;
    private readonly Native.WinEventDelegate _winEventProc;
    private readonly uint _taskbarCreatedMessage = Native.RegisterWindowMessage("TaskbarCreated");
    private readonly uint _appBarMessage = Native.RegisterWindowMessage("TaskbarMonitor.AppBarNotify");

    private IntPtr _hwnd;
    private IntPtr _foregroundHook;
    private IntPtr _locationHook;
    private uint _hookedExplorerPid;
    private bool _appBarRegistered;
    private bool _shellReportsFullscreen;
    private bool _ownedByTaskbar;
    private bool _dragging;
    private bool _wantVisible;
    private int _snappedY;

    public OverlayWindow(SettingsViewModel vm)
    {
        _vm = vm;
        _menu = new OverlayMenu(vm);
        _winEventProc = OnWinEvent;

        Title = "Монитор ресурсов — оверлей";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        UseLayoutRounding = true;
        Focusable = false;
        Left = -32000;
        Top = -32000;
        Topmost = vm.AlwaysOnTop;
        Content = _view;

        _repositionTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(40) };
        _repositionTimer.Tick += (_, _) =>
        {
            _repositionTimer.Stop();
            Reposition();
            UpdateVisibility();
        };

        MouseEnter += (_, _) => _view.IsHot = true;
        MouseLeave += (_, _) => _view.IsHot = false;
        MouseLeftButtonDown += OnLeftButtonDown;
        MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            _menu.Show(_vm.ActiveSurface);
        };
        SizeChanged += (_, _) => ScheduleReposition();

        _vm.OverlayChanged += ApplySettings;
        _vm.SnapshotUpdated += OnSnapshot;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        Native.SetExStyle(_hwnd, Native.GetExStyle(_hwnd) | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);

        _foregroundHook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _winEventProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
        AttachToShell();
    }

    public void Start()
    {
        _view.ApplyPalette(_vm.Palette, animate: false);
        RebuildContent();
        Show();
        ApplySettings();
    }

    private void AttachToShell()
    {
        _taskbar.Refresh();
        ApplyOwner();
        RegisterAppBar();
        HookExplorer();
        ScheduleReposition();
    }

    private void ApplyOwner()
    {
        bool own = _vm.SnapToTaskbar && _taskbar.IsValid;
        Native.SetWindowLongPtr(_hwnd, Native.GWLP_HWNDPARENT, own ? _taskbar.Taskbar : IntPtr.Zero);
        _ownedByTaskbar = own;
    }

    // Registering as an app bar (without reserving space) makes the shell report fullscreen apps to us.
    private void RegisterAppBar()
    {
        if (_appBarRegistered)
            return;
        var data = new Native.APPBARDATA { cbSize = Marshal.SizeOf<Native.APPBARDATA>(), hWnd = _hwnd, uCallbackMessage = _appBarMessage };
        _appBarRegistered = Native.SHAppBarMessage(Native.ABM_NEW, ref data) != UIntPtr.Zero;
    }

    private void UnregisterAppBar()
    {
        if (!_appBarRegistered)
            return;
        var data = new Native.APPBARDATA { cbSize = Marshal.SizeOf<Native.APPBARDATA>(), hWnd = _hwnd };
        Native.SHAppBarMessage(Native.ABM_REMOVE, ref data);
        _appBarRegistered = false;
    }

    // Taskbar and tray moves (auto-hide, icons added to the tray, resolution changes) come from explorer.
    private void HookExplorer()
    {
        uint pid = _taskbar.ExplorerProcessId;
        if (pid == _hookedExplorerPid && _locationHook != IntPtr.Zero)
            return;
        if (_locationHook != IntPtr.Zero)
            Native.UnhookWinEvent(_locationHook);
        _locationHook = pid == 0 ? IntPtr.Zero : Native.SetWinEventHook(Native.EVENT_OBJECT_LOCATIONCHANGE, Native.EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero, _winEventProc, pid, 0, Native.WINEVENT_OUTOFCONTEXT);
        _hookedExplorerPid = pid;
    }

    protected override void OnClosed(EventArgs e)
    {
        _vm.OverlayChanged -= ApplySettings;
        _vm.SnapshotUpdated -= OnSnapshot;
        _repositionTimer.Stop();
        UnregisterAppBar();
        if (_foregroundHook != IntPtr.Zero)
            Native.UnhookWinEvent(_foregroundHook);
        if (_locationHook != IntPtr.Zero)
            Native.UnhookWinEvent(_locationHook);
        base.OnClosed(e);
    }

    private void ApplySettings()
    {
        if (_hwnd == IntPtr.Zero)
            return;
        if (_ownedByTaskbar != (_vm.SnapToTaskbar && _taskbar.IsValid))
            ApplyOwner();
        Topmost = _vm.AlwaysOnTop;
        _view.ApplyPalette(_vm.Palette, animate: true);
        RebuildContent();
        _view.Relayout();
        ScheduleReposition();
    }

    private void OnSnapshot()
    {
        RebuildContent();
        if (!_taskbar.IsValid && _taskbar.Refresh())
            AttachToShell();
        UpdateVisibility();
    }

    private void RebuildContent() => _view.Update(MetricLayout.Build(_vm.Snapshot, _vm.Settings), _vm.Settings);

    private void ScheduleReposition()
    {
        _repositionTimer.Stop();
        _repositionTimer.Start();
    }

    private void Reposition()
    {
        if (_hwnd == IntPtr.Zero || _dragging)
            return;
        if (!Native.GetWindowRect(_hwnd, out var self))
            return;

        if (_vm.SnapToTaskbar && _taskbar.TryGetTaskbarRect(out var bar) && TaskbarLocator.IsHorizontal(bar))
        {
            double scale = _taskbar.Dpi();
            double barHeight = bar.Height / scale;
            if (double.IsNaN(_view.BarHeight) || Math.Abs(_view.BarHeight - barHeight) > 0.5)
            {
                // Resize first; the next pass positions with the final size.
                _view.BarHeight = barHeight;
                ScheduleReposition();
                return;
            }
            int offset = (int)Math.Round(_vm.Offset * scale);
            int x = _vm.Anchor == OverlayAnchor.LeftEdge
                ? bar.Left + offset
                : _taskbar.TrayLeft(bar) - self.Width - offset;
            x = Math.Clamp(x, bar.Left, Math.Max(bar.Left, bar.Right - self.Width));
            _snappedY = bar.Top + (bar.Height - self.Height) / 2;
            Move(x, _snappedY);
        }
        else
        {
            if (!double.IsNaN(_view.BarHeight))
            {
                _view.BarHeight = double.NaN;
                ScheduleReposition();
                return;
            }
            var (x, y) = FreePosition(self);
            Move(x, y);
        }
    }

    private (int X, int Y) FreePosition(Native.RECT self)
    {
        int x = _vm.Settings.FreeX, y = _vm.Settings.FreeY;
        var screen = new Native.RECT
        {
            Left = (int)SystemParameters.VirtualScreenLeft,
            Top = (int)SystemParameters.VirtualScreenTop,
        };
        // Virtual screen size is in DIPs of the primary monitor; convert to pixels.
        var dpi = VisualTreeHelper.GetDpi(this);
        screen.Right = screen.Left + (int)(SystemParameters.VirtualScreenWidth * dpi.DpiScaleX);
        screen.Bottom = screen.Top + (int)(SystemParameters.VirtualScreenHeight * dpi.DpiScaleY);

        if (x == int.MinValue || y == int.MinValue)
        {
            // First time floating: just above the taskbar, left of the tray.
            if (_taskbar.TryGetTaskbarRect(out var bar))
            {
                x = _taskbar.TrayLeft(bar) - self.Width - 16;
                y = bar.Top - self.Height - 8;
            }
            else
            {
                x = screen.Right - self.Width - 32;
                y = screen.Bottom - self.Height - 64;
            }
        }
        x = Math.Clamp(x, screen.Left, Math.Max(screen.Left, screen.Right - self.Width));
        y = Math.Clamp(y, screen.Top, Math.Max(screen.Top, screen.Bottom - self.Height));
        return (x, y);
    }

    private void Move(int x, int y) =>
        Native.SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);

    private void UpdateVisibility()
    {
        bool show = _vm.ShowOverlay && _vm.HasEnabledMetrics;
        if (show && _vm.SnapToTaskbar)
            show = _taskbar.TryGetTaskbarRect(out var bar) && _taskbar.IsOnScreen(bar);
        if (show && _vm.HideOnFullscreen)
            show = !IsFullscreenActive();
        SetVisible(show);
    }

    private bool IsFullscreenActive()
    {
        var foreground = Native.GetForegroundWindow();
        if (foreground == _hwnd || FullscreenDetector.IsShellWindow(foreground))
            return false;
        return _shellReportsFullscreen || FullscreenDetector.IsFullscreen(foreground, _taskbar.Taskbar);
    }

    private void SetVisible(bool visible)
    {
        if (visible == _wantVisible && (visible == IsVisible))
            return;
        _wantVisible = visible;
        bool animate = SystemParameters.ClientAreaAnimation;
        if (visible)
        {
            if (!IsVisible)
            {
                Opacity = animate ? 0 : 1;
                Show();
                ScheduleReposition();
            }
            BeginAnimation(OpacityProperty, animate ? new DoubleAnimation(1, FadeDuration) : null);
            Opacity = 1;
        }
        else if (IsVisible)
        {
            if (!animate)
            {
                Hide();
                return;
            }
            var fade = new DoubleAnimation(0, FadeDuration);
            fade.Completed += (_, _) =>
            {
                if (!_wantVisible)
                    Hide();
            };
            BeginAnimation(OpacityProperty, fade);
        }
    }

    private void ReassertTopmost()
    {
        if (!_vm.AlwaysOnTop || !IsVisible)
            return;
        var foreground = Native.GetForegroundWindow();
        var cls = Native.GetClassName(foreground);
        // Re-asserting while the taskbar itself is active makes it flicker.
        if (cls is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            return;
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
    }

    private void OnLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (e.ClickCount == 2)
        {
            switch (_vm.DoubleClick)
            {
                case DoubleClickAction.TaskManager:
                    _vm.OpenTaskManagerCommand.Execute(null);
                    break;
                case DoubleClickAction.Settings:
                    _vm.OpenSettingsCommand.Execute(null);
                    break;
            }
            return;
        }
        if (_vm.LockPosition)
            return;

        _dragging = true;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            _dragging = false;
        }
        SaveDraggedPosition();
    }

    private void SaveDraggedPosition()
    {
        if (!Native.GetWindowRect(_hwnd, out var self))
            return;
        if (_vm.SnapToTaskbar && _taskbar.TryGetTaskbarRect(out var bar))
        {
            double scale = _taskbar.Dpi();
            int offsetPx = _vm.Anchor == OverlayAnchor.LeftEdge
                ? self.Left - bar.Left
                : _taskbar.TrayLeft(bar) - self.Right;
            _vm.SetPosition((int)Math.Round(Math.Max(0, offsetPx) / scale), null, null);
            ScheduleReposition();
        }
        else
        {
            _vm.SetPosition(null, self.Left, self.Top);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case Native.WM_MOUSEACTIVATE:
                handled = true;
                return Native.MA_NOACTIVATE;

            case Native.WM_WINDOWPOSCHANGING when _dragging && _vm.SnapToTaskbar:
                // While snapped, dragging only slides the overlay along the taskbar.
                var pos = Marshal.PtrToStructure<Native.WINDOWPOS>(lParam);
                if ((pos.flags & Native.SWP_NOMOVE) == 0 && _taskbar.TryGetTaskbarRect(out var bar) && Native.GetWindowRect(_hwnd, out var me))
                {
                    pos.y = _snappedY;
                    pos.x = Math.Clamp(pos.x, bar.Left, Math.Max(bar.Left, bar.Right - me.Width));
                    Marshal.StructureToPtr(pos, lParam, false);
                }
                break;

            case Native.WM_DISPLAYCHANGE:
                ScheduleReposition();
                break;

            case Native.WM_SETTINGCHANGE when wParam == Native.SPI_SETWORKAREA:
                ScheduleReposition();
                break;
        }

        if (msg == (int)_taskbarCreatedMessage && _taskbarCreatedMessage != 0)
        {
            // Explorer restarted: every handle and registration is stale.
            _appBarRegistered = false;
            _shellReportsFullscreen = false;
            Dispatcher.BeginInvoke(AttachToShell);
        }
        else if (msg == (int)_appBarMessage && _appBarMessage != 0 && (uint)wParam == Native.ABN_FULLSCREENAPP)
        {
            _shellReportsFullscreen = lParam != IntPtr.Zero;
            Dispatcher.BeginInvoke(UpdateVisibility);
        }
        return IntPtr.Zero;
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (eventType == Native.EVENT_SYSTEM_FOREGROUND)
        {
            UpdateVisibility();
            ReassertTopmost();
        }
        else if (eventType == Native.EVENT_OBJECT_LOCATIONCHANGE && idObject == Native.OBJID_WINDOW
                 && (hwnd == _taskbar.Taskbar || hwnd == _taskbar.Tray))
        {
            ScheduleReposition();
        }
    }
}
