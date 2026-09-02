using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PowerPlugin.App.Ui;

/// <summary>
/// One statistics tile: a caption, a large value, a secondary line and an optional footnote.
/// </summary>
internal sealed class StatCard : Border
{
    private readonly TextBlock _value;
    private readonly TextBlock _secondary;
    private readonly TextBlock _footnote;

    public StatCard(string caption, Color accent)
    {
        Background = Theme.SurfaceBrush;
        BorderBrush = Theme.BorderBrush;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(10);
        Padding = new Thickness(16, 14, 16, 14);

        var accentBrush = new SolidColorBrush(accent);
        accentBrush.Freeze();

        _value = new TextBlock
        {
            FontFamily = Theme.DisplayFont,
            FontSize = 26,
            FontWeight = FontWeights.SemiBold,
            Foreground = accentBrush,
            Margin = new Thickness(0, 4, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        _secondary = Theme.Body(string.Empty);
        _secondary.Margin = new Thickness(0, 2, 0, 0);

        _footnote = Theme.Muted(string.Empty, 10.5);
        _footnote.Margin = new Thickness(0, 6, 0, 0);
        _footnote.TextWrapping = TextWrapping.Wrap;

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = accentBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });
        header.Children.Add(Theme.Muted(caption.ToUpperInvariant(), 10.5));

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(_value);
        stack.Children.Add(_secondary);
        stack.Children.Add(_footnote);

        Child = stack;
    }

    public void Update(string value, string secondary, string footnote = "")
    {
        _value.Text = value;
        _secondary.Text = secondary;
        _footnote.Text = footnote;
        _footnote.Visibility = footnote.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
