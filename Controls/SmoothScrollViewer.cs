using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TaskbarMonitor.Controls;

/// <summary>
/// ScrollViewer whose mouse wheel glides to the target offset instead of jumping, like the one in WinUI.
/// Each notch moves the target; the offset eases toward it every frame, so fast wheel spins and
/// touchpad streams of small deltas both stay smooth. Wheel input a nested control already handled is left alone.
/// </summary>
public sealed class SmoothScrollViewer : ScrollViewer
{
    private const double PixelsPerLine = 34;
    private const double TimeConstantMs = 70;

    private double _target;
    private double _position;
    private bool _running;
    private TimeSpan _lastFrame;

    public SmoothScrollViewer() => Unloaded += (_, _) => Stop();

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (e.Handled || !Motion.Enabled)
        {
            base.OnMouseWheel(e);
            return;
        }
        e.Handled = true;
        if (ScrollableHeight <= 0)
            return;

        if (!_running)
            _target = _position = VerticalOffset;
        _target = Math.Clamp(_target - e.Delta / (double)Mouse.MouseWheelDeltaForOneLine * WheelStep(), 0, ScrollableHeight);
        Start();
    }

    private double WheelStep()
    {
        int lines = SystemParameters.WheelScrollLines;
        return lines < 0 ? ViewportHeight : Math.Max(1, lines) * PixelsPerLine;
    }

    /// <summary>Jumps to the top at once, cancelling a glide in progress.</summary>
    public void JumpToTop()
    {
        Stop();
        ScrollToTop();
    }

    // A click on the scroll bar or a key press takes over from the glide.
    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        Stop();
        base.OnPreviewMouseDown(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        Stop();
        base.OnPreviewKeyDown(e);
    }

    protected override void OnScrollChanged(ScrollChangedEventArgs e)
    {
        base.OnScrollChanged(e);
        if (_running && (e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0))
            _target = Math.Clamp(_target, 0, ScrollableHeight);
    }

    private void Start()
    {
        if (_running)
            return;
        _running = true;
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }

    private void Stop()
    {
        if (!_running)
            return;
        _running = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        // Rendering can be raised more than once per frame.
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _lastFrame)
            return;
        double elapsed = _lastFrame == TimeSpan.Zero ? 1000.0 / 60 : Math.Min((now - _lastFrame).TotalMilliseconds, 100);
        _lastFrame = now;

        double remaining = _target - _position;
        if (Math.Abs(remaining) < 0.5)
        {
            _position = _target;
            Stop();
        }
        else
        {
            _position += remaining * (1 - Math.Exp(-elapsed / TimeConstantMs));
        }
        ScrollToVerticalOffset(_position);
    }
}
