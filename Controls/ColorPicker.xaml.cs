using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TaskbarMonitor.Theming;

namespace TaskbarMonitor.Controls;

public partial class ColorPicker : UserControl
{
    // Readable text colors for both taskbar themes, then the 48 accent colors Windows offers.
    private static readonly string[] TextColors =
        ["#FFFFFF", "#E0E0E0", "#9E9E9E", "#1B1B1B", "#000000", "#60CDFF", "#99EBFF", "#005FB8", "#003E92", "#6CCB5F", "#0F7B0F", "#FCE100", "#9D5D00"];

    private static readonly string[] WindowsColors =
    [
        "#FFB900", "#FF8C00", "#F7630C", "#CA5010", "#DA3B01", "#EF6950", "#D13438", "#FF4343",
        "#E74856", "#E81123", "#EA005E", "#C30052", "#E3008C", "#BF0077", "#C239B3", "#9A0089",
        "#0078D4", "#0063B1", "#8E8CD8", "#6B69D6", "#8764B8", "#744DA9", "#B146C2", "#881798",
        "#0099BC", "#2D7D9A", "#00B7C3", "#038387", "#00B294", "#018574", "#00CC6A", "#10893E",
        "#7A7574", "#5D5A58", "#68768A", "#515C6B", "#567C73", "#486860", "#498205", "#107C10",
        "#767676", "#4C4A48", "#69797E", "#4A5459", "#647C64", "#525E54", "#847545", "#7E735F",
    ];

    public static readonly DependencyProperty HexProperty = DependencyProperty.Register(
        nameof(Hex), typeof(string), typeof(ColorPicker),
        new FrameworkPropertyMetadata("#FFFFFF", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnHexChanged));

    public static readonly DependencyProperty CaptionProperty =
        DependencyProperty.Register(nameof(Caption), typeof(string), typeof(ColorPicker), new PropertyMetadata(""));

    private static readonly DependencyPropertyKey SwatchBrushKey = DependencyProperty.RegisterReadOnly(
        nameof(SwatchBrush), typeof(Brush), typeof(ColorPicker), new PropertyMetadata(Brushes.White));

    public static readonly DependencyProperty SwatchBrushProperty = SwatchBrushKey.DependencyProperty;

    public ColorPicker()
    {
        InitializeComponent();
        TextSwatches.ItemsSource = TextColors.Select(ToBrush).ToList();
        WindowsSwatches.ItemsSource = WindowsColors.Select(ToBrush).ToList();
        Flyout.Opened += (_, _) =>
        {
            HexBox.Text = Hex;
            HexBox.Focus();
            HexBox.SelectAll();
        };
    }

    public string Hex
    {
        get => (string)GetValue(HexProperty);
        set => SetValue(HexProperty, value);
    }

    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    public Brush SwatchBrush => (Brush)GetValue(SwatchBrushProperty);

    private static void OnHexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (ColorPicker)d;
        picker.SetValue(SwatchBrushKey, ToBrush(e.NewValue as string ?? "#FFFFFF"));
        if (picker.Flyout.IsOpen)
            picker.HexBox.Text = e.NewValue as string ?? "";
    }

    private static SolidColorBrush ToBrush(string hex)
    {
        var brush = new SolidColorBrush(Contrast.Parse(hex, Colors.White));
        brush.Freeze();
        return brush;
    }

    private void OnSwatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Background: SolidColorBrush brush })
        {
            Hex = Contrast.ToHex(brush.Color);
            Toggle.IsChecked = false;
            Toggle.Focus();
        }
    }

    private void OnHexKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Commit();
            Toggle.IsChecked = false;
            Toggle.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Toggle.IsChecked = false;
            Toggle.Focus();
            e.Handled = true;
        }
    }

    private void OnHexCommit(object sender, RoutedEventArgs e) => Commit();

    private void Commit()
    {
        if (Contrast.TryParse(HexBox.Text, out var color))
            Hex = Contrast.ToHex(color);
        else
            HexBox.Text = Hex;
    }
}
