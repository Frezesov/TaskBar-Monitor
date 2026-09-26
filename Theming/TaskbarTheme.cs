using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace TaskbarMonitor.Theming;

// The seven shades Windows derives from the user's accent color (Settings → Personalization → Colors).
public readonly record struct AccentPalette(Color Light3, Color Light2, Color Light1, Color Accent, Color Dark1, Color Dark2, Color Dark3)
{
    public Color[] All => [Light3, Light2, Light1, Accent, Dark1, Dark2, Dark3];

    public static readonly string[] Names = ["Light 3", "Light 2", "Light 1", "Акцент", "Dark 1", "Dark 2", "Dark 3"];

    // Windows' default blue, used when nothing can be read.
    public static AccentPalette Default { get; } = new(
        Color.FromRgb(0x99, 0xEB, 0xFF), Color.FromRgb(0x4C, 0xC2, 0xFF), Color.FromRgb(0x00, 0x91, 0xF8),
        Color.FromRgb(0x00, 0x78, 0xD4),
        Color.FromRgb(0x00, 0x67, 0xC0), Color.FromRgb(0x00, 0x3E, 0x92), Color.FromRgb(0x00, 0x1A, 0x68));
}

/// <summary>
/// Snapshot of everything that decides how the taskbar looks.
/// The taskbar follows the *Windows* mode (SystemUsesLightTheme), not the app mode (AppsUseLightTheme):
/// with the "Custom" color mode they differ, and only the former matters for the overlay.
/// </summary>
public sealed record TaskbarTheme(
    bool SystemLight,
    bool AppsLight,
    bool AccentOnTaskbar,
    bool Transparency,
    bool HighContrast,
    AccentPalette Accent,
    Color HighContrastWindow,
    Color HighContrastText,
    Color HighContrastHotTrack)
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AccentKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";

    // Must run on the UI thread: SystemColors and SystemParameters are WPF caches.
    public static TaskbarTheme Read()
    {
        bool systemLight = false, appsLight = false, prevalence = false, transparency = true;
        AccentPalette? accent = null;
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey))
            {
                systemLight = ReadFlag(key, "SystemUsesLightTheme", false);
                appsLight = ReadFlag(key, "AppsUseLightTheme", false);
                prevalence = ReadFlag(key, "ColorPrevalence", false);
                transparency = ReadFlag(key, "EnableTransparency", true);
            }
            using (var key = Registry.CurrentUser.OpenSubKey(AccentKey))
                accent = ParsePalette(key?.GetValue("AccentPalette") as byte[]);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            Core.ErrorLog.Write(ex);
        }

        return new TaskbarTheme(
            systemLight,
            appsLight,
            prevalence,
            transparency,
            SystemParameters.HighContrast,
            accent ?? FromSystemColors(),
            SystemColors.WindowColor,
            SystemColors.WindowTextColor,
            SystemColors.HotTrackColor);
    }

    private static bool ReadFlag(RegistryKey? key, string name, bool fallback) =>
        key?.GetValue(name) is int value ? value != 0 : fallback;

    // AccentPalette is 8 × RGBA: Light3, Light2, Light1, Accent, Dark1, Dark2, Dark3, (complementary).
    private static AccentPalette? ParsePalette(byte[]? data)
    {
        if (data is null || data.Length < 28)
            return null;
        Color At(int i) => Color.FromRgb(data[i * 4], data[i * 4 + 1], data[i * 4 + 2]);
        return new AccentPalette(At(0), At(1), At(2), At(3), At(4), At(5), At(6));
    }

    private static AccentPalette FromSystemColors()
    {
        try
        {
            return new AccentPalette(
                SystemColors.AccentColorLight3, SystemColors.AccentColorLight2, SystemColors.AccentColorLight1,
                SystemColors.AccentColor,
                SystemColors.AccentColorDark1, SystemColors.AccentColorDark2, SystemColors.AccentColorDark3);
        }
        catch (Exception ex)
        {
            Core.ErrorLog.Write(ex);
            return AccentPalette.Default;
        }
    }
}
