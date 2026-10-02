using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PowerPlugin.App.Ui;

/// <summary>
/// One key figure, built like a cell of the website's meta bar: a capitalised monospace label
/// above a monospace value, with the supporting lines in the body colour underneath.
/// <para>
/// The accent marks the cell rather than colouring the number - a square in front of the label,
/// the way the section headings do it. Nine such cards all shouting in their own colour would
/// undo the restraint the palette is built on.
/// </para>
/// </summary>
internal sealed class StatCard : Border
{
    private readonly TextBlock _value;
    private readonly TextBlock _secondary;
    private readonly TextBlock _footnote;

    public StatCard(string caption, Color accent)
    {
        Background = Theme.PanelBrush;
        BorderBrush = Theme.BorderBrush;
        BorderThickness = new Thickness(1);
        Padding = new Thickness(18, 16, 18, 16);

        var accentBrush = new SolidColorBrush(accent);
        accentBrush.Freeze();

        _value = Theme.Value(string.Empty, 25);
        _value.Margin = new Thickness(0, 10, 0, 0);

        _secondary = Theme.Body(string.Empty, 12.5);
        _secondary.Margin = new Thickness(0, 7, 0, 0);
        _secondary.TextWrapping = TextWrapping.Wrap;

        _footnote = Theme.Muted(string.Empty, 11);
        _footnote.Margin = new Thickness(0, 7, 0, 0);
        _footnote.TextWrapping = TextWrapping.Wrap;

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new Border
        {
            Width = 7,
            Height = 7,
            Background = accentBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        });
        header.Children.Add(Theme.Label(caption, 10.5));

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
