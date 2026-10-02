using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using PowerPlugin.Core.Model;

namespace PowerPlugin.App.Ui;

/// <summary>
/// The visual language, taken from the achterberg.dev stylesheet so PowerPlugin sits in the same
/// family as the website and Budgetmaxxer.
/// <para>
/// Three things carry that identity. The palette is near monochrome - a very dark ground, a
/// barely lighter panel, hairline borders - with a single orange accent used sparingly as a
/// marker. Nothing is rounded: the whole stylesheet contains exactly one border radius, and it
/// belongs to a status dot. And the interface chrome - labels, values, buttons, badges - is set in
/// a monospace face in capitals, while running text stays in the sans.
/// </para>
/// </summary>
internal static class Theme
{
    // ---- Tokens -------------------------------------------------------------------
    // Values from the :root block of assets/style.css (dark theme).

    public static readonly Color Background = FromHex("#0b0b0d");
    public static readonly Color Panel = FromHex("#101013");
    public static readonly Color BorderColor = FromHex("#242427");
    public static readonly Color BorderSoft = FromHex("#1b1b1e");
    public static readonly Color Text = FromHex("#ededec");
    public static readonly Color TextBody = FromHex("#a2a2a7");
    public static readonly Color TextMuted = FromHex("#6f6f77");
    public static readonly Color Accent = FromHex("#f97316");
    public static readonly Color Good = FromHex("#4ade80");

    /// <summary>Hover and selection wash, <c>--accent-soft</c>.</summary>
    public static readonly Color AccentSoft = Color.FromArgb(0x10, 0xf9, 0x73, 0x16);

    /// <summary>
    /// The stylesheet has no warning colours of its own, so the accent doubles as the middle step.
    /// The tray icon needs a third step above it; that red is taken from the same palette family
    /// the accent and the green come from.
    /// </summary>
    public static readonly Color Warn = Accent;
    public static readonly Color Danger = FromHex("#ef4444");

    /// <summary>Grid line of the page background, <c>--grid</c>.</summary>
    public static readonly Color GridLine = Color.FromArgb(0x08, 0xff, 0xff, 0xff);

    /// <summary>Edge length of the background grid, from <c>background-size: 84px 84px</c>.</summary>
    public const double GridCell = 84;

    public static readonly Brush BackgroundBrush = Frozen(new SolidColorBrush(Background));
    public static readonly Brush PanelBrush = Frozen(new SolidColorBrush(Panel));
    public static readonly Brush BorderBrush = Frozen(new SolidColorBrush(BorderColor));
    public static readonly Brush BorderSoftBrush = Frozen(new SolidColorBrush(BorderSoft));
    public static readonly Brush TextBrush = Frozen(new SolidColorBrush(Text));
    public static readonly Brush TextBodyBrush = Frozen(new SolidColorBrush(TextBody));
    public static readonly Brush TextMutedBrush = Frozen(new SolidColorBrush(TextMuted));
    public static readonly Brush AccentBrush = Frozen(new SolidColorBrush(Accent));
    public static readonly Brush AccentSoftBrush = Frozen(new SolidColorBrush(AccentSoft));
    public static readonly Brush GoodBrush = Frozen(new SolidColorBrush(Good));
    public static readonly Brush WarnBrush = Frozen(new SolidColorBrush(Warn));
    public static readonly Brush DangerBrush = Frozen(new SolidColorBrush(Danger));

    /// <summary>
    /// Running text. Inter is the website's face; where it is not installed Windows falls back
    /// through the list, and Segoe UI Variable is close enough in proportion to keep the look.
    /// </summary>
    public static readonly FontFamily SansFont =
        new("Inter, Segoe UI Variable Text, Segoe UI, Arial");

    /// <summary>
    /// Labels, values, buttons and badges. Cascadia Mono ships with Windows 11 and stands in for
    /// JetBrains Mono closely enough.
    /// </summary>
    public static readonly FontFamily MonoFont =
        new("JetBrains Mono, Cascadia Mono, Consolas, Courier New");

    // ---- Background ----------------------------------------------------------------

    /// <summary>
    /// The faint 84 pixel grid of the website, as a tiling brush for the window background.
    /// </summary>
    public static Brush CreateGridBrush()
    {
        var drawing = new DrawingGroup();

        drawing.Children.Add(new GeometryDrawing(
            BackgroundBrush,
            null,
            new RectangleGeometry(new Rect(0, 0, GridCell, GridCell))));

        var pen = new Pen(Frozen(new SolidColorBrush(GridLine)), 1);
        var lines = new GeometryGroup();
        lines.Children.Add(new LineGeometry(new Point(0, 0.5), new Point(GridCell, 0.5)));
        lines.Children.Add(new LineGeometry(new Point(0.5, 0), new Point(0.5, GridCell)));
        drawing.Children.Add(new GeometryDrawing(null, pen, lines));

        var brush = new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, GridCell, GridCell),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };

        return Frozen(brush);
    }

    // ---- Category colours -----------------------------------------------------------

    /// <summary>
    /// Colour of a component category in lists and charts.
    /// <para>
    /// The stylesheet itself only knows the accent and one green, which is not enough to tell nine
    /// categories apart. Both of its colours are from the same open palette family, so the
    /// remaining hues are taken from there too and stay at the same saturation - the breakdown
    /// gains colour coding without leaving the register of the design.
    /// </para>
    /// </summary>
    public static Color ColorFor(ComponentCategory category) => category switch
    {
        ComponentCategory.Cpu => Accent,                 // orange, the leading consumer
        ComponentCategory.Gpu => FromHex("#a78bfa"),     // violet
        ComponentCategory.Memory => Good,                // the stylesheet's green
        ComponentCategory.Storage => FromHex("#fbbf24"), // amber
        ComponentCategory.Mainboard => FromHex("#7a7a84"),
        ComponentCategory.Cooling => FromHex("#38bdf8"), // sky
        ComponentCategory.Display => FromHex("#f472b6"), // pink
        ComponentCategory.PowerSupply => Danger,
        _ => TextMuted,
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

    // ---- Text ------------------------------------------------------------------------

    public static TextBlock Title(string text, double size = 15) => new()
    {
        Text = text,
        FontFamily = SansFont,
        FontSize = size,
        FontWeight = FontWeights.Bold,
        Foreground = TextBrush,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    public static TextBlock Body(string text, double size = 13.5) => new()
    {
        Text = text,
        FontFamily = SansFont,
        FontSize = size,
        Foreground = TextBodyBrush,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    public static TextBlock Muted(string text, double size = 12) => new()
    {
        Text = text,
        FontFamily = SansFont,
        FontSize = size,
        Foreground = TextMutedBrush,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>
    /// A small capitalised label in the monospace face, the website's <c>.meta-label</c>.
    /// <para>
    /// The original widens these by about .15em. WPF offers no letter spacing on a text block, and
    /// the usual trick of padding with thin spaces fails in a monospace face, where every glyph -
    /// space included - takes a full advance width. The mono capitals carry the effect on their
    /// own, so the tracking is dropped rather than faked badly.
    /// </para>
    /// </summary>
    public static TextBlock Label(string text, double size = 10.5, Brush? foreground = null) => new()
    {
        Text = text.ToUpperInvariant(),
        FontFamily = MonoFont,
        FontSize = size,
        Foreground = foreground ?? TextMutedBrush,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>A measured value, the website's <c>.meta-value</c>: monospace, in the text colour.</summary>
    public static TextBlock Value(string text, double size = 14, Brush? foreground = null) => new()
    {
        Text = text,
        FontFamily = MonoFont,
        FontSize = size,
        Foreground = foreground ?? TextBrush,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>
    /// The section heading of the website: a small accent square, a capitalised label, and a rule
    /// running out to the right edge.
    /// </summary>
    public static UIElement SectionHead(string label)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var square = new Border
        {
            Width = 7,
            Height = 7,
            Background = AccentBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        grid.Children.Add(square);

        TextBlock text = Label(label, 11.5);
        text.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var rule = new Border
        {
            Height = 1,
            Background = BorderBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
        };
        Grid.SetColumn(rule, 2);
        grid.Children.Add(rule);

        AutomationProperties.SetName(grid, label);
        return grid;
    }

    // ---- Surfaces ---------------------------------------------------------------------

    /// <summary>
    /// A panel with a hairline border and square corners, the website's bordered box.
    /// </summary>
    /// <param name="ticks">
    /// Adds the blueprint corner marks. Off by default on purpose: the stylesheet makes them an
    /// opt-in class rather than part of every box, and on adjacent cards the marks of neighbours
    /// end up almost touching.
    /// </param>
    public static Border Card(UIElement child, Thickness? padding = null, bool ticks = false)
    {
        Border card = ticks ? new TickBorder() : new Border();

        card.Background = PanelBrush;
        card.BorderBrush = BorderBrush;
        card.BorderThickness = new Thickness(1);
        card.Padding = padding ?? new Thickness(22, 20, 22, 20);
        card.Child = child;

        return card;
    }

    /// <summary>A card with a heading above its content.</summary>
    public static Border TitledCard(string heading, UIElement content, Thickness? padding = null, bool ticks = false)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        UIElement head = SectionHead(heading);
        grid.Children.Add(head);

        var host = new ContentControl { Content = content, Margin = new Thickness(0, 18, 0, 0) };
        Grid.SetRow(host, 1);
        grid.Children.Add(host);

        return Card(grid, padding, ticks);
    }

    /// <summary>
    /// A bordered tag, the website's <c>.tag</c>: monospace capitals, hairline border, no fill.
    /// The colour tints border and text rather than adding a filled pill.
    /// </summary>
    public static Border Badge(string text, Color color)
    {
        var foreground = Frozen(new SolidColorBrush(color));
        var border = Frozen(new SolidColorBrush(Color.FromArgb(0x55, color.R, color.G, color.B)));

        return new Border
        {
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(7, 2, 7, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text.ToUpperInvariant(),
                FontFamily = MonoFont,
                FontSize = 9.5,
                Foreground = foreground,
            },
        };
    }

    // ---- Controls ----------------------------------------------------------------------

    /// <summary>
    /// The website's button: no fill, a hairline border, monospace capitals. Hovering tints the
    /// text and border with the accent and washes the surface - the primary variant simply starts
    /// with the accent border.
    /// </summary>
    public static Button Button(string caption, bool primary = false)
    {
        var button = new Button
        {
            Content = caption.ToUpperInvariant(),
            FontFamily = MonoFont,
            FontSize = 11.5,
            Foreground = primary ? TextBrush : TextBodyBrush,
            Background = Brushes.Transparent,
            BorderBrush = primary ? AccentBrush : BorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(17, 10, 17, 11),
            Cursor = System.Windows.Input.Cursors.Hand,
            MinWidth = 96,
            Template = BuildButtonTemplate(),
        };

        AutomationProperties.SetName(button, caption);
        return button;
    }

    private static ControlTemplate BuildButtonTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border), "Root");
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.ForegroundProperty, AccentBrush));
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, AccentBrush, "Root"));
        hover.Setters.Add(new Setter(Border.BackgroundProperty, AccentSoftBrush, "Root"));
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
        FontFamily = SansFont,
        FontSize = 13,
        Foreground = TextBodyBrush,
        VerticalContentAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 6, 0, 6),
    };

    public static RadioButton Radio(string caption, string groupName) => new()
    {
        Content = caption,
        FontFamily = SansFont,
        FontSize = 13,
        Foreground = TextBodyBrush,
        Margin = new Thickness(0, 5, 0, 5),
        GroupName = groupName,
    };

    /// <summary>The website's input: panel ground, hairline border, square, accent border on focus.</summary>
    public static TextBox TextBox(string text, double width = 110)
    {
        var box = new TextBox
        {
            Text = text,
            Width = width,
            FontFamily = MonoFont,
            FontSize = 12.5,
            Foreground = TextBrush,
            CaretBrush = AccentBrush,
            Background = PanelBrush,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(11, 8, 11, 9),
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        box.GotKeyboardFocus += (_, _) => box.BorderBrush = AccentBrush;
        box.LostKeyboardFocus += (_, _) => box.BorderBrush = BorderBrush;
        return box;
    }

    // ---- Helpers -------------------------------------------------------------------------

    private static Color FromHex(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}

/// <summary>
/// A bordered box with the website's blueprint corner ticks: a small accent cross just outside the
/// top left and bottom right corners. Drawn rather than composed so the marks can sit outside the
/// border without disturbing the layout.
/// </summary>
internal sealed class TickBorder : Border
{
    private const double ArmLength = 9;
    private const double Offset = 4.5;

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);

        var pen = new Pen(Theme.AccentBrush, 1);
        pen.Freeze();

        Draw(context, pen, new Point(-Offset, -Offset));
        Draw(context, pen, new Point(ActualWidth + Offset, ActualHeight + Offset));
    }

    private static void Draw(DrawingContext context, Pen pen, Point centre)
    {
        double half = ArmLength / 2;
        double x = Math.Round(centre.X) + 0.5;
        double y = Math.Round(centre.Y) + 0.5;

        context.DrawLine(pen, new Point(x - half, y), new Point(x + half, y));
        context.DrawLine(pen, new Point(x, y - half), new Point(x, y + half));
    }
}
