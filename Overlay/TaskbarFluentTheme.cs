using System.Collections.ObjectModel;
using System.Windows;
using TaskbarMonitor.Core;
using TaskbarMonitor.Theming;

namespace TaskbarMonitor.Overlay;

/// <summary>
/// Fluent light/dark dictionaries for the overlay's popups. They follow the taskbar's theme (Windows mode),
/// not the app mode, so the menu and the details look like the taskbar's own flyouts even with "Custom" colors.
/// </summary>
internal static class TaskbarFluentTheme
{
    private const string FluentLight = "pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.Light.xaml";
    private const string FluentDark = "pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.Dark.xaml";

    private static readonly Dictionary<bool, ResourceDictionary?> Cache = [];

    /// <summary>
    /// Swaps the merged theme when the surface changed since <paramref name="applied"/>.
    /// High contrast keeps the app theme: WPF's Fluent HC dictionary already follows system colors.
    /// </summary>
    public static void Apply(Collection<ResourceDictionary> merged, TaskbarSurface surface, ref bool? applied)
    {
        bool? light = surface switch
        {
            TaskbarSurface.Light => true,
            TaskbarSurface.Dark or TaskbarSurface.Accent => false,
            _ => null,
        };
        if (light == applied)
            return;
        applied = light;
        merged.Clear();
        if (light is { } isLight && Load(isLight) is { } dictionary)
            merged.Add(dictionary);
    }

    private static ResourceDictionary? Load(bool light)
    {
        if (Cache.TryGetValue(light, out var cached))
            return cached;
        ResourceDictionary? dictionary = null;
        try
        {
            dictionary = new ResourceDictionary { Source = new Uri(light ? FluentLight : FluentDark, UriKind.Absolute) };
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.Windows.Markup.XamlParseException)
        {
            ErrorLog.Write(ex);
        }
        Cache[light] = dictionary;
        return dictionary;
    }
}
