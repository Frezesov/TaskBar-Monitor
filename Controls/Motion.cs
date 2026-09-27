using System.Windows;
using System.Windows.Media.Animation;

namespace TaskbarMonitor.Controls;

/// <summary>
/// Motion tokens of the settings window, modeled on WinUI's page refresh and NavigationView indicator.
/// Everything falls back to instant changes when "Animation effects" is off in Windows.
/// </summary>
internal static class Motion
{
    public static bool Enabled => SystemParameters.ClientAreaAnimation;

    public static readonly Duration PageExit = new(TimeSpan.FromMilliseconds(80));
    public static readonly Duration PageEnter = new(TimeSpan.FromMilliseconds(320));
    public static readonly Duration PageFade = new(TimeSpan.FromMilliseconds(160));
    public static readonly Duration Indicator = new(TimeSpan.FromMilliseconds(260));

    /// <summary>How far below its resting place a new page starts, in DIPs.</summary>
    public const double PageOffset = 40;

    /// <summary>Fluent "fast out, slow in": most of the distance is covered at once, then it settles.</summary>
    public static IEasingFunction Decelerate { get; } = new SplineEase(0, 0, 0, 1);

    public static IEasingFunction Accelerate { get; } = new SplineEase(1, 0, 1, 1);

    private sealed class SplineEase : IEasingFunction
    {
        private readonly KeySpline _spline;

        public SplineEase(double x1, double y1, double x2, double y2)
        {
            _spline = new KeySpline(x1, y1, x2, y2);
            _spline.Freeze();
        }

        public double Ease(double normalizedTime) => _spline.GetSplineProgress(normalizedTime);
    }
}
