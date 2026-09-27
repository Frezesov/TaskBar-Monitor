using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TaskbarMonitor.Core;

namespace TaskbarMonitor.Controls;

/// <summary>
/// Shows a shortcut as key caps, like PowerToys. Click it and press a combination to change it; Esc cancels.
/// While it listens, <see cref="IsCapturing"/> is true so the owner can release the global registration.
/// </summary>
public sealed class HotkeyBox : Button
{
    public static readonly DependencyProperty GestureProperty = DependencyProperty.Register(
        nameof(Gesture), typeof(HotkeyGesture), typeof(HotkeyBox),
        new FrameworkPropertyMetadata(HotkeyGesture.Default, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnStateChanged));

    public static readonly DependencyProperty IsCapturingProperty = DependencyProperty.Register(
        nameof(IsCapturing), typeof(bool), typeof(HotkeyBox),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnStateChanged));

    private string? _hint;

    public HotkeyBox()
    {
        // Implicit styles match the exact type, so the Fluent button look has to be asked for.
        SetResourceReference(StyleProperty, typeof(Button));
        HorizontalContentAlignment = HorizontalAlignment.Left;
        Padding = new Thickness(6, 5, 10, 5);
        // Key caps and the prompt differ in height: keep the row from jumping when recording starts.
        MinHeight = 36;
        Loaded += (_, _) => Render();
        IsEnabledChanged += (_, _) => Render();
    }

    public HotkeyGesture Gesture
    {
        get => (HotkeyGesture)GetValue(GestureProperty);
        set => SetValue(GestureProperty, value);
    }

    public bool IsCapturing
    {
        get => (bool)GetValue(IsCapturingProperty);
        set => SetValue(IsCapturingProperty, value);
    }

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((HotkeyBox)d).Render();

    protected override void OnClick()
    {
        base.OnClick();
        _hint = null;
        IsCapturing = !IsCapturing;
        if (IsCapturing)
            Focus();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        IsCapturing = false;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!IsCapturing)
        {
            base.OnPreviewKeyDown(e);
            return;
        }
        // Handled here, so neither the button (Space, Enter) nor the window (Esc) reacts to the keys being recorded.
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        if (key == Key.Escape && modifiers == ModifierKeys.None)
        {
            IsCapturing = false;
            return;
        }
        if (HotkeyGesture.IsModifierKey(key))
        {
            Render(new HotkeyGesture(modifiers, Key.None));
            return;
        }
        var gesture = new HotkeyGesture(modifiers, key);
        if (!gesture.IsValid)
        {
            _hint = "Добавьте Ctrl, Alt или Win";
            Render();
            return;
        }
        Gesture = gesture;
        IsCapturing = false;
    }

    private void Render() => Render(IsCapturing ? new HotkeyGesture(ModifierKeys.None, Key.None) : Gesture);

    private void Render(HotkeyGesture shown)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Opacity = IsEnabled ? 1 : 0.45 };
        foreach (var part in shown.Parts)
            panel.Children.Add(KeyCap(part));

        string? text = IsCapturing ? _hint ?? (shown.Parts.Count == 0 ? "Нажмите сочетание клавиш…" : "…") : null;
        if (text is not null)
        {
            var prompt = new TextBlock { Text = text, Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
            prompt.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            panel.Children.Add(prompt);
        }
        else
        {
            var edit = new Glyph { Text = "", FontSize = 12, Margin = new Thickness(6, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            edit.SetResourceReference(TextBlock.FontFamilyProperty, "IconFontFamily");
            edit.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            panel.Children.Add(edit);
        }
        Content = panel;
        AutomationProperties.SetName(this, IsCapturing
            ? "Нажмите новое сочетание клавиш, Esc — отмена"
            : $"Сочетание клавиш {Gesture.DisplayText}. Нажмите, чтобы изменить");
    }

    private static Border KeyCap(string label)
    {
        var text = new TextBlock { Text = label, FontSize = 12.5, FontWeight = FontWeights.SemiBold };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextOnAccentFillColorPrimaryBrush");
        var cap = new Border
        {
            MinWidth = 28,
            Padding = new Thickness(8, 3, 8, 4),
            Margin = new Thickness(2, 0, 2, 0),
            CornerRadius = new CornerRadius(4),
            Child = text,
        };
        text.HorizontalAlignment = HorizontalAlignment.Center;
        cap.SetResourceReference(Border.BackgroundProperty, "AccentFillColorDefaultBrush");
        return cap;
    }
}
