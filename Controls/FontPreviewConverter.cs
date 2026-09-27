using System.Globalization;
using System.Windows;
using System.Windows.Data;
using TaskbarMonitor.Core;

namespace TaskbarMonitor.Controls;

/// <summary>Font name → the family the overlay would use, so the font list shows each name in its own face.</summary>
public sealed class FontPreviewConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string name ? OverlayFonts.Resolve(name) : DependencyProperty.UnsetValue;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
