using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PowerPlugin.Core.Model;

namespace PowerPlugin.App.Ui;

/// <summary>
/// Central place for colours, fonts and the small building blocks the windows are made of.
/// The application is built in code rather than XAML, so the visual language lives here.
/// </summary>
internal static class Theme
{
    public static readonly Color Background = Color.FromRgb(0x0F, 0x11, 0x15);
    public static readonly Color Surface = Color.FromRgb(0x18, 0x1B, 0x22);
    public static readonly Color SurfaceRaised = Color.FromRgb(0x1F, 0x23, 0x2C);
    public static readonly Color BorderColor = Color.FromRgb(0x2A, 0x2F, 0x3A);
    public static readonly Color Text = Color.FromRgb(0xE7, 0xEB, 0xF3);
    public static readonly Color TextMuted = Color.FromRgb(0x93, 0x9C, 0xB0);
    public static readonly Color Accent = Color.FromRgb(0x4C, 0x8D, 0xFF);
    public static readonly Color Good = Color.FromRgb(0x35, 0xC7, 0x8A);
    public static readonly Color Warn = Color.FromRgb(0xF5, 0xA5, 0x24);
    public static readonly Color Danger = Color.FromRgb(0xE5, 0x54, 0x4B);

    public static readonly Brush BackgroundBrush = Freeze(new SolidColorBrush(Background));
    public static readonly Brush SurfaceBrush = Freeze(new SolidColorBrush(Surface));
    public static readonly Brush SurfaceRaisedBrush = Freeze(new SolidColorBrush(SurfaceRaised));
    public static readonly Brush BorderBrush = Freeze(new SolidColorBrush(BorderColor));
    public static readonly Brush TextBrush = Freeze(new SolidColorBrush(Text));
    public static readonly Brush TextMutedBrush = Freeze(new SolidColorBrush(TextMuted));
    public static readonly Brush AccentBrush = Freeze(new SolidColorBrush(Accent));
    public static readonly Brush GoodBrush = Freeze(new SolidColorBrush(Good));
    public static readonly Brush WarnBrush = Freeze(new SolidColorBrush(Warn));
    public static readonly Brush DangerBrush = Freeze(new SolidColorBrush(Danger));

    public static readonly FontFamily UiFont = new("Segoe UI Variable Text, Segoe UI, Arial");
    public static readonly FontFamily DisplayFont = new("Segoe UI Variable Display, Segoe UI Semibold, Segoe UI, Arial");

    /// <summary>Colour used for a component category in lists and charts.</summary>
    public static Color ColorFor(ComponentCategory category) => category switch
    {
        ComponentCategory.Cpu => Color.FromRgb(0x4C, 0x8D, 0xFF),
        ComponentCategory.Gpu => Color.FromRgb(0xA6, 0x6B, 0xFF),
        ComponentCategory.Memory => Color.FromRgb(0x23, 0xC7, 0xA0),
        ComponentCategory.Storage => Color.FromRgb(0xF5, 0xA5, 0x24),
        ComponentCategory.Mainboard => Color.FromRgb(0x7A, 0x86, 0xA0),
        ComponentCategory.Cooling => Color.FromRgb(0x4F, 0xC3, 0xF7),
        ComponentCategory.Display => Color.FromRgb(0xFF, 0x7A, 0xB6),
        ComponentCategory.PowerSupply => Color.FromRgb(0xE5, 0x54, 0x4B),
        _ => Color.FromRgb(0x8A, 0x93, 0xA6),
    };

    public static string LabelFor(ComponentCategory category) => category switch
    {
        ComponentCategory.Cpu => "Prozessor",
        ComponentCategory.Gpu => "Grafik",
        ComponentCategory.Memory => "Arbeitsspeicher",
        ComponentCategory.Storage => "Datenträger",
        ComponentCategory.Mainboard => "Mainboard",
        ComponentCategory.Cooling => "Kühlung",
        ComponentCategory.Display => "Bildschirm",
        ComponentCategory.PowerSupply => "Netzteil",
        _ => "Sonstiges",
    };

    /// <summary>Colour of the tray icon and the headline value for a given load.</summary>
    public static Color LoadColor(double watts, double greenThreshold, double amberThreshold)
    {
        if (watts <= greenThreshold)
        {
            return Good;
        }

        return watts <= amberThreshold ? Warn : Danger;
    }

    // ---- Building blocks ----------------------------------------------------------

    public static TextBlock Title(string text, double size = 15) => new()
    {
        Text = text,
        FontFamily = DisplayFont,
        FontSize = size,
        FontWeight = FontWeights.SemiBold,
        Foreground = TextBrush,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    public static TextBlock Body(string text, double size = 12.5) => new()
    {
        Text = text,
        FontFamily = UiFont,
        FontSize = size,
        Foreground = TextBrush,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    public static TextBlock Muted(string text, double size = 11.5) => new()
    {
        Text = text,
        FontFamily = UiFont,
        FontSize = size,
        Foreground = TextMutedBrush,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>A card with a subtle border, used for every block on the page.</summary>
    public static Border Card(UIElement child, Thickness? padding = null) => new()
    {
        Background = SurfaceBrush,
        BorderBrush = BorderBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = padding ?? new Thickness(16),
        Child = child,
    };

    /// <summary>A small rounded label, e.g. "Sensor" or "Geschätzt".</summary>
    public static Border Badge(string text, Color color)
    {
        var background = new SolidColorBrush(Color.FromArgb(0x2E, color.R, color.G, color.B));
        background.Freeze();

        var foreground = new SolidColorBrush(color);
        foreground.Freeze();

        return new Border
        {
            Background = background,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 1, 6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                FontFamily = UiFont,
                FontSize = 10.5,
                Foreground = foreground,
                FontWeight = FontWeights.SemiBold,
            },
        };
    }

    /// <summary>Flat button matching the dark surface style.</summary>
    public static Button Button(string caption, bool primary = false)
    {
        var button = new Button
        {
            Content = caption,
            FontFamily = UiFont,
            FontSize = 12.5,
            Foreground = primary ? Freeze(new SolidColorBrush(Colors.White)) : TextBrush,
            Background = primary ? AccentBrush : SurfaceRaisedBrush,
            BorderBrush = primary ? AccentBrush : BorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 7, 14, 8),
            Cursor = System.Windows.Input.Cursors.Hand,
            MinWidth = 90,
        };

        button.Template = BuildButtonTemplate();
        return button;
    }

    /// <summary>
    /// The default WPF button template paints its own chrome, which clashes with the dark
    /// surface, so buttons get a minimal template with rounded corners and a hover state.
    /// </summary>
    private static ControlTemplate BuildButtonTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border), "Root");
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.85, "Root"));
        template.Triggers.Add(hover);

        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45, "Root"));
        template.Triggers.Add(disabled);

        template.Seal();
        return template;
    }

    public static CheckBox CheckBox(string caption, bool isChecked) => new()
    {
        Content = caption,
        IsChecked = isChecked,
        FontFamily = UiFont,
        FontSize = 12.5,
        Foreground = TextBrush,
        VerticalContentAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 6, 0, 6),
    };

    public static TextBox TextBox(string text, double width = 110) => new()
    {
        Text = text,
        Width = width,
        FontFamily = UiFont,
        FontSize = 12.5,
        Foreground = TextBrush,
        CaretBrush = TextBrush,
        Background = SurfaceRaisedBrush,
        BorderBrush = BorderBrush,
        BorderThickness = new Thickness(1),
        Padding = new Thickness(8, 5, 8, 5),
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
