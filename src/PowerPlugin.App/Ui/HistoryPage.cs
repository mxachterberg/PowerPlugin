using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PowerPlugin.Core.Configuration;
using PowerPlugin.Core.Model;
using PowerPlugin.Core.Statistics;
using PowerPlugin.Core.Storage;

namespace PowerPlugin.App.Ui;

/// <summary>
/// Long term view: consumption per day over the last 30 days, the totals of the whole
/// recording and which components the energy actually went into.
/// </summary>
internal sealed class HistoryPage : Grid
{
    private readonly DailyEnergyChart _chart = new();
    private readonly StackPanel _dayList = new();
    private readonly StackPanel _componentList = new();
    private readonly TextBlock _totals;

    private AppSettings _settings;

    public HistoryPage(AppSettings settings)
    {
        _settings = settings;

        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.15, GridUnitType.Star) });

        // ---- Daily chart ------------------------------------------------------------
        var chartGrid = new Grid();
        chartGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        chartGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        chartGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        chartGrid.Children.Add(Theme.Title("Verbrauch der letzten 30 Tage"));

        _totals = Theme.Muted(string.Empty);
        _totals.Margin = new Thickness(0, 2, 0, 10);
        _totals.TextWrapping = TextWrapping.Wrap;
        SetRow(_totals, 1);
        chartGrid.Children.Add(_totals);

        _chart.MinHeight = 150;
        SetRow(_chart, 2);
        chartGrid.Children.Add(_chart);

        Border chartCard = Theme.Card(chartGrid);
        chartCard.Margin = new Thickness(0, 0, 0, 14);
        SetRow(chartCard, 0);
        Children.Add(chartCard);

        // ---- Tables -----------------------------------------------------------------
        var tables = new Grid();
        tables.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tables.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Border dayCard = BuildListCard(
            "Einzelne Tage",
            "Verbrauch, Durchschnitt und Spitzenwert je Kalendertag.",
            _dayList);
        dayCard.Margin = new Thickness(0, 0, 7, 0);
        SetColumn(dayCard, 0);
        tables.Children.Add(dayCard);

        Border componentCard = BuildListCard(
            "Energie nach Komponente",
            "Summierter Energieanteil der letzten 30 Tage.",
            _componentList);
        componentCard.Margin = new Thickness(7, 0, 0, 0);
        SetColumn(componentCard, 1);
        tables.Children.Add(componentCard);

        SetRow(tables, 1);
        Children.Add(tables);
    }

    public void ApplySettings(AppSettings settings) => _settings = settings;

    private static Border BuildListCard(string title, string subtitle, StackPanel list)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new StackPanel();
        header.Children.Add(Theme.Title(title));

        TextBlock caption = Theme.Muted(subtitle);
        caption.Margin = new Thickness(0, 2, 0, 8);
        header.Children.Add(caption);
        grid.Children.Add(header);

        var scroller = new ScrollViewer
        {
            Content = list,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 8, 0),
        };
        SetRow(scroller, 1);
        grid.Children.Add(scroller);

        return Theme.Card(grid);
    }

    public void Update(EnergyStatistics statistics)
    {
        _chart.SetData(statistics.RecentDays);

        string period = statistics.FirstDay is { } first
            ? $"Aufzeichnung seit {first:dd.MM.yyyy} · {statistics.DaysWithData} Tage mit Messwerten"
            : "Noch keine Aufzeichnung vorhanden";

        _totals.Text =
            $"{period} · Gesamt {Formatting.KilowattHours(statistics.AllTime.EnergyKilowattHours)} " +
            $"({Formatting.Money((decimal)statistics.AllTime.EnergyKilowattHours * statistics.PricePerKilowattHour, _settings.CurrencySymbol)}) · " +
            $"Ø {Formatting.KilowattHours(statistics.AverageDailyKilowattHours)} pro Tag · " +
            $"Ø Leistung im Betrieb {Formatting.Watts(statistics.AllTime.AverageWatts)}";

        BuildDayRows(statistics);
        BuildComponentRows(statistics);
    }

    private void BuildDayRows(EnergyStatistics statistics)
    {
        _dayList.Children.Clear();

        double maximum = Math.Max(0.0001, statistics.RecentDays.Count == 0
            ? 0.0001
            : statistics.RecentDays.Max(d => d.EnergyKilowattHours));

        foreach (DailyEnergy day in statistics.RecentDays.Reverse())
        {
            if (day.CoveredSeconds <= 0)
            {
                continue;
            }

            _dayList.Children.Add(BuildRow(
                Formatting.RelativeDay(day.Day),
                Formatting.KilowattHours(day.EnergyKilowattHours),
                $"Ø {Formatting.Watts(day.AverageWatts)} · Peak {Formatting.Watts(day.PeakWatts)} · {Formatting.Hours(day.CoveredHours)} erfasst",
                day.EnergyKilowattHours / maximum,
                Theme.Accent));
        }

        if (_dayList.Children.Count == 0)
        {
            _dayList.Children.Add(Theme.Muted("Sobald der erste Tag aufgezeichnet ist, erscheint er hier."));
        }
    }

    private void BuildComponentRows(EnergyStatistics statistics)
    {
        _componentList.Children.Clear();

        double total = statistics.ComponentBreakdown.Sum(c => c.EnergyWattHours);

        if (total <= 0)
        {
            _componentList.Children.Add(Theme.Muted("Die Aufschlüsselung wird stündlich gespeichert und erscheint nach der ersten vollen Stunde."));
            return;
        }

        foreach (ComponentEnergy component in statistics.ComponentBreakdown.Take(15))
        {
            var category = (ComponentCategory)component.Category;
            double share = component.EnergyWattHours / total;

            _componentList.Children.Add(BuildRow(
                component.Name,
                Formatting.KilowattHours(component.EnergyWattHours / 1000.0),
                $"{share * 100:0.#} % der Gesamtenergie · Ø {Formatting.Watts(component.AverageWatts)}",
                share,
                Theme.ColorFor(category)));
        }
    }

    private static UIElement BuildRow(string title, string value, string detail, double fraction, Color color)
    {
        var grid = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());

        TextBlock titleBlock = Theme.Body(title, 12.5);
        grid.Children.Add(titleBlock);

        TextBlock valueBlock = Theme.Body(value, 12.5);
        valueBlock.FontWeight = FontWeights.SemiBold;
        valueBlock.HorizontalAlignment = HorizontalAlignment.Right;
        SetColumn(valueBlock, 1);
        grid.Children.Add(valueBlock);

        var brush = new SolidColorBrush(color);
        brush.Freeze();

        var bar = new Border
        {
            Background = brush,
            CornerRadius = new CornerRadius(2),
            Height = 4,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        var track = new Border
        {
            Background = Theme.SurfaceRaisedBrush,
            CornerRadius = new CornerRadius(2),
            Height = 4,
            Margin = new Thickness(0, 5, 0, 4),
            Child = bar,
        };
        track.SizeChanged += (_, _) =>
            bar.Width = Math.Max(2, track.ActualWidth * Math.Clamp(fraction, 0, 1));

        SetRow(track, 1);
        SetColumnSpan(track, 2);
        grid.Children.Add(track);

        TextBlock detailBlock = Theme.Muted(detail, 10.5);
        SetRow(detailBlock, 2);
        SetColumnSpan(detailBlock, 2);
        grid.Children.Add(detailBlock);

        return grid;
    }
}
