using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace TaskbarMonitor.Controls;

/// <summary>
/// One line of a settings card: icon, title, description, the control on the right
/// and an optional <see cref="Detail"/> shown under the text (sliders, color pickers).
/// </summary>
public sealed class SettingRow : ContentControl
{
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(SettingRow), new PropertyMetadata(null));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(SettingRow), new PropertyMetadata(null, OnTitleChanged));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingRow), new PropertyMetadata(null));

    public static readonly DependencyProperty DetailProperty =
        DependencyProperty.Register(nameof(Detail), typeof(object), typeof(SettingRow), new PropertyMetadata(null));

    public string? Glyph
    {
        get => (string?)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Description
    {
        get => (string?)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public object? Detail
    {
        get => GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    // Screen readers announce the control on the right by the row title.
    private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SettingRow { Content: DependencyObject control } && string.IsNullOrEmpty(AutomationProperties.GetName(control)))
            AutomationProperties.SetName(control, e.NewValue as string ?? "");
    }

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        if (newContent is DependencyObject control && !string.IsNullOrEmpty(Title) && string.IsNullOrEmpty(AutomationProperties.GetName(control)))
            AutomationProperties.SetName(control, Title);
    }
}
