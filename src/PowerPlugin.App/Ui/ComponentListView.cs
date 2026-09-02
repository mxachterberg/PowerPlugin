using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PowerPlugin.Core.Model;

namespace PowerPlugin.App.Ui;

/// <summary>
/// The live breakdown of every consumer above the reporting threshold.
/// <para>
/// Rows are recycled by component key instead of being rebuilt on every tick, so the list does
/// not flicker and the update stays cheap even at a one second sampling interval.
/// </para>
/// </summary>
internal sealed class ComponentListView : StackPanel
{
    private readonly Dictionary<string, ComponentRow> _rows = new(StringComparer.Ordinal);

    public void Update(PowerSnapshot snapshot)
    {
        double maximum = snapshot.Components.Length > 0
            ? Math.Max(1, snapshot.Components.Max(c => c.Watts))
            : 1;

        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (int index = 0; index < snapshot.Components.Length; index++)
        {
            PowerComponent component = snapshot.Components[index];
            seen.Add(component.Key);

            if (!_rows.TryGetValue(component.Key, out ComponentRow? row))
            {
                row = new ComponentRow();
                _rows[component.Key] = row;
                Children.Add(row);
            }

            row.Update(component, maximum, snapshot.TotalWatts);

            // Keep the visual order in sync with the descending sort of the snapshot.
            int currentIndex = Children.IndexOf(row);
            if (currentIndex != index && index < Children.Count)
            {
                Children.RemoveAt(currentIndex);
                Children.Insert(index, row);
            }
        }

        // Remove rows for components that dropped below the threshold.
        foreach (string key in _rows.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            Children.Remove(_rows[key]);
            _rows.Remove(key);
        }
    }

    private sealed class ComponentRow : Grid
    {
        private readonly TextBlock _name;
        private readonly TextBlock _detail;
        private readonly TextBlock _watts;
        private readonly TextBlock _share;
        private readonly Border _bar;
        private readonly Border _barTrack;
        private readonly Border _badgeHost;
        private readonly SolidColorBrush _accent = new();

        public ComponentRow()
        {
            Margin = new Thickness(0, 5, 0, 5);
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition());
            RowDefinitions.Add(new RowDefinition());
            RowDefinitions.Add(new RowDefinition());

            _name = Theme.Body(string.Empty, 13);
            SetColumn(_name, 0);
            SetRow(_name, 0);
            Children.Add(_name);

            _watts = new TextBlock
            {
                FontFamily = Theme.DisplayFont,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.TextBrush,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            SetColumn(_watts, 1);
            SetRow(_watts, 0);
            Children.Add(_watts);

            _bar = new Border
            {
                Background = _accent,
                CornerRadius = new CornerRadius(3),
                Height = 6,
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = 0,
            };

            _barTrack = new Border
            {
                Background = Theme.SurfaceRaisedBrush,
                CornerRadius = new CornerRadius(3),
                Height = 6,
                Margin = new Thickness(0, 6, 0, 4),
                Child = _bar,
            };
            SetColumn(_barTrack, 0);
            SetRow(_barTrack, 1);
            SetColumnSpan(_barTrack, 2);
            Children.Add(_barTrack);
            _barTrack.SizeChanged += (_, _) => ApplyBarWidth();

            var footer = new StackPanel { Orientation = Orientation.Horizontal };

            _badgeHost = new Border { Margin = new Thickness(0, 0, 8, 0) };
            footer.Children.Add(_badgeHost);

            _detail = Theme.Muted(string.Empty, 10.5);
            _detail.VerticalAlignment = VerticalAlignment.Center;
            footer.Children.Add(_detail);

            SetColumn(footer, 0);
            SetRow(footer, 2);
            Children.Add(footer);

            _share = Theme.Muted(string.Empty, 10.5);
            _share.HorizontalAlignment = HorizontalAlignment.Right;
            _share.VerticalAlignment = VerticalAlignment.Center;
            SetColumn(_share, 1);
            SetRow(_share, 2);
            Children.Add(_share);
        }

        private double _fraction;

        public void Update(PowerComponent component, double maximumWatts, double totalWatts)
        {
            Color color = Theme.ColorFor(component.Category);
            _accent.Color = color;

            _name.Text = component.Name;
            _watts.Text = Formatting.Watts(component.Watts);
            _detail.Text = component.Detail ?? Theme.LabelFor(component.Category);
            _share.Text = totalWatts > 0 ? $"{component.Watts / totalWatts * 100:0} % vom Gesamtwert" : string.Empty;

            _badgeHost.Child = Theme.Badge(component.Source.ToDisplayString(), color);

            ToolTip = $"{component.Name}\n{Theme.LabelFor(component.Category)} · {component.Source.ToDisplayString()}\n{component.Detail}";

            _fraction = Math.Clamp(component.Watts / maximumWatts, 0, 1);
            ApplyBarWidth();
        }

        private void ApplyBarWidth()
        {
            double available = _barTrack.ActualWidth;
            _bar.Width = available > 0 ? Math.Max(2, available * _fraction) : 0;
        }
    }
}
