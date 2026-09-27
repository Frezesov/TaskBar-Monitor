using System.Windows.Media;

namespace TaskbarMonitor.Core;

/// <summary>
/// Fonts offered for the overlay. Inter, Montserrat, Roboto and JetBrains Mono ship inside the app,
/// so they look the same on every PC; the rest are system fonts and are listed only when installed.
/// </summary>
public static class OverlayFonts
{
    public const string Default = "Segoe UI Variable Text";

    private static readonly string[] Candidates =
    [
        "Segoe UI Variable Text", "Segoe UI Variable Display", "Segoe UI", "Inter", "Montserrat", "Roboto",
        "Calibri", "Verdana", "Bahnschrift", "Times New Roman", "JetBrains Mono", "Cascadia Mono", "Consolas",
    ];

    private static readonly HashSet<string> Bundled = new(StringComparer.Ordinal) { "Inter", "Montserrat", "Roboto", "JetBrains Mono" };

    private static readonly Uri PackRoot = new("pack://application:,,,/");

    private static readonly Dictionary<string, FontFamily> Families = new(StringComparer.Ordinal);

    /// <summary>Fonts that can be used on this PC, in menu order.</summary>
    public static IReadOnlyList<string> Available() => Candidates.Where(IsAvailable).ToList();

    /// <summary>Family for a font name from the settings, with Segoe UI for missing glyphs.</summary>
    public static FontFamily Resolve(string name)
    {
        if (!Families.TryGetValue(name, out var family))
        {
            family = Bundled.Contains(name)
                ? new FontFamily(PackRoot, $"./Assets/Fonts/#{name}, Segoe UI")
                : new FontFamily(name + ", Segoe UI");
            Families[name] = family;
        }
        return family;
    }

    private static bool IsAvailable(string name) =>
        Bundled.Contains(name)
        || Fonts.SystemFontFamilies.Any(f => f.FamilyNames.Values.Any(v => string.Equals(v, name, StringComparison.OrdinalIgnoreCase)))
        || name.StartsWith("Segoe UI Variable", StringComparison.Ordinal)
           && Fonts.SystemFontFamilies.Any(f => f.Source.StartsWith("Segoe UI Variable", StringComparison.Ordinal));
}
