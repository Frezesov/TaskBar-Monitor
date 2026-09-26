using System.Windows.Media;
using TaskbarMonitor.Core;

namespace TaskbarMonitor.Theming;

public enum TaskbarSurface { Dark, Light, Accent, HighContrast }

/// <summary>Every color the overlay draws with, already adapted to one taskbar surface.</summary>
public sealed record OverlayPalette(
    TaskbarSurface Surface,
    Color Background,
    Color ContrastReference,
    Color Label,
    Color Value,
    Color Caution,
    Color Critical,
    Color Pod,
    Color PodStroke,
    Color Hover,
    Color Plate)
{
    public double LabelContrast => Contrast.Ratio(Label, ContrastReference);
    public double ValueContrast => Contrast.Ratio(Value, ContrastReference);
}

public static class TaskbarPalette
{
    // What the taskbar roughly looks like (used for previews) ...
    private static readonly Color DarkTaskbar = Color.FromRgb(0x20, 0x20, 0x20);
    private static readonly Color LightTaskbar = Color.FromRgb(0xEE, 0xEE, 0xEE);
    // ... and a slightly less favorable variant for contrast checks: acrylic lets the wallpaper tint it.
    private const double WorstCaseTint = 0.07;

    // Fluent text colors (TextFillColorPrimary) and system status colors per theme.
    private static readonly Color DarkText = Colors.White;
    private static readonly Color LightText = Color.FromRgb(0x1B, 0x1B, 0x1B);
    private static readonly Color DarkCaution = Color.FromRgb(0xFC, 0xE1, 0x00);
    private static readonly Color DarkCritical = Color.FromRgb(0xFF, 0x99, 0xA4);
    private static readonly Color LightCaution = Color.FromRgb(0x9D, 0x5D, 0x00);
    private static readonly Color LightCritical = Color.FromRgb(0xC4, 0x2B, 0x1C);

    public static TaskbarSurface ResolveSurface(TaskbarTheme theme, TaskbarThemeOverride mode) => mode switch
    {
        TaskbarThemeOverride.Light => TaskbarSurface.Light,
        TaskbarThemeOverride.Dark => TaskbarSurface.Dark,
        _ when theme.HighContrast => TaskbarSurface.HighContrast,
        // Windows keeps ColorPrevalence=1 after switching to light mode although the taskbar is no longer tinted.
        _ when theme.SystemLight => TaskbarSurface.Light,
        _ when theme.AccentOnTaskbar => TaskbarSurface.Accent,
        _ => TaskbarSurface.Dark,
    };

    public static OverlayPalette Build(TaskbarTheme theme, AppSettings settings) =>
        Build(ResolveSurface(theme, settings.ThemeOverride), theme, settings);

    public static OverlayPalette Build(TaskbarSurface surface, TaskbarTheme theme, AppSettings settings)
    {
        if (surface == TaskbarSurface.HighContrast)
            return BuildHighContrast(theme);

        var a = theme.Accent;
        var background = surface switch
        {
            TaskbarSurface.Light => LightTaskbar,
            TaskbarSurface.Accent => a.Dark2,
            _ => DarkTaskbar,
        };
        var reference = surface switch
        {
            TaskbarSurface.Light => Contrast.Mix(background, Colors.Black, WorstCaseTint),
            TaskbarSurface.Accent => Contrast.Mix(a.Dark2, a.Dark1, 0.5),
            _ => Contrast.Mix(background, Colors.White, WorstCaseTint),
        };
        bool light = surface == TaskbarSurface.Light;

        // Accent shades in order of preference: saturated enough to read as the accent, light/dark enough to read as text.
        Color[] primaryShades = surface switch
        {
            TaskbarSurface.Light => [a.Dark1, a.Dark2, a.Dark3],
            TaskbarSurface.Accent => [a.Light3, a.Light2],
            _ => [a.Light2, a.Light3, a.Light1],
        };
        Color[] secondaryShades = surface switch
        {
            TaskbarSurface.Light => [a.Dark2, a.Dark3],
            TaskbarSurface.Accent => [a.Light3],
            _ => [a.Light3, a.Light2],
        };
        var accentLabel = Contrast.FirstReadable(reference, Contrast.TextMinimum, primaryShades);
        var accentValue = Contrast.FirstReadable(reference, Contrast.TextMinimum, secondaryShades);
        var autoValue = light ? LightText : DarkText;

        var value = settings.ValueColor switch
        {
            ValueColorMode.Accent => accentValue,
            ValueColorMode.Custom => Contrast.Parse(light ? settings.CustomValueLight : settings.CustomValueDark, autoValue),
            _ => autoValue,
        };
        var label = settings.LabelColor switch
        {
            LabelColorMode.SameAsValue => value,
            LabelColorMode.Custom => Contrast.Parse(light ? settings.CustomLabelLight : settings.CustomLabelDark, accentLabel),
            _ => accentLabel,
        };

        return new OverlayPalette(
            surface,
            background,
            reference,
            label,
            value,
            Contrast.Ensure(light ? LightCaution : DarkCaution, reference, Contrast.TextMinimum),
            Contrast.Ensure(light ? LightCritical : DarkCritical, reference, Contrast.TextMinimum),
            Pod: light ? Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF),
            PodStroke: light ? Color.FromArgb(0x14, 0x00, 0x00, 0x00) : Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF),
            Hover: light ? Color.FromArgb(0x0C, 0x00, 0x00, 0x00) : Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF),
            Plate: Contrast.WithAlpha(background, 0.92));
    }

    // In high contrast only system colors are allowed; custom and status colors are ignored.
    private static OverlayPalette BuildHighContrast(TaskbarTheme theme)
    {
        var bg = theme.HighContrastWindow;
        var text = theme.HighContrastText;
        var label = Contrast.Ratio(theme.HighContrastHotTrack, bg) >= Contrast.TextMinimum ? theme.HighContrastHotTrack : text;
        return new OverlayPalette(
            TaskbarSurface.HighContrast, bg, bg, label, text, text, text,
            Pod: Colors.Transparent,
            PodStroke: text,
            Hover: Contrast.WithAlpha(label, 0.25),
            Plate: bg);
    }
}
