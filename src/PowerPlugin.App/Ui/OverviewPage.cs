using System.Windows;
using System.Windows.Controls;
using PowerPlugin.Core.Configuration;
using PowerPlugin.Core.Model;
using PowerPlugin.Core.Statistics;

namespace PowerPlugin.App.Ui;

/// <summary>
/// The landing page: the four requested key figures on top, the live breakdown of every
/// consumer on the left and the power curve of the last minutes on the right.
/// </summary>
internal sealed class OverviewPage : Grid
{
    private readonly StatCard _dailyAverageCard = new("Tagesdurchschnitt", Theme.Accent);
    private readonly StatCard _peakCard = new("Peak", Theme.Danger);
    private readonly StatCard _monthCard = new("Monatsdurchschnitt", Theme.Good);
    private readonly StatCard _yearCard = new("Jahresprognose", Theme.Warn);

    private readonly ComponentListView _components = new();
    private readonly LivePowerChart _liveChart = new();
    private readonly TextBlock _componentsCaption;
    private readonly TextBlock _todaySummary;
    private readonly TextBlock _systemSummary;

    private AppSettings _settings;

    public OverviewPage(AppSettings settings)
    {
        _settings = settings;

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        Children.Add(BuildStatRow());

        var body = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.45, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        SetRow(body, 1);

        // ---- Left: live breakdown -------------------------------------------------
        _componentsCaption = Theme.Muted("Alle Verbraucher über 1 W, absteigend sortiert.");
        _componentsCaption.Margin = new Thickness(0, 2, 0, 10);
        _componentsCaption.TextWrapping = TextWrapping.Wrap;

        var componentsStack = new StackPanel();
        componentsStack.Children.Add(Theme.Title("Aktuelle Leistungsverteilung"));
        componentsStack.Children.Add(_componentsCaption);

        var componentsGrid = new Grid();
        componentsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        componentsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        componentsGrid.Children.Add(componentsStack);

        var scroller = new ScrollViewer
        {
            Content = _components,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 8, 0),
        };
        SetRow(scroller, 1);
        componentsGrid.Children.Add(scroller);

        Border componentsCard = Theme.Card(componentsGrid);
        componentsCard.Margin = new Thickness(0, 0, 7, 0);
        SetColumn(componentsCard, 0);
        body.Children.Add(componentsCard);

        // ---- Right: live chart and summaries ---------------------------------------
        var right = new Grid { Margin = new Thickness(7, 0, 0, 0) };
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var chartGrid = new Grid();
        chartGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        chartGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        chartGrid.Children.Add(Theme.Title("Verlauf der letzten Minuten"));

        _liveChart.Margin = new Thickness(0, 10, 0, 0);
        _liveChart.MinHeight = 140;
        SetRow(_liveChart, 1);
        chartGrid.Children.Add(_liveChart);

        Border chartCard = Theme.Card(chartGrid);
        SetRow(chartCard, 0);
        right.Children.Add(chartCard);

        _todaySummary = Theme.Body(string.Empty);
        _todaySummary.TextWrapping = TextWrapping.Wrap;
        _todaySummary.LineHeight = 20;
        var todayStack = new StackPanel();
        todayStack.Children.Add(Theme.Title("Heute"));
        todayStack.Children.Add(WithTopMargin(_todaySummary, 8));

        Border todayCard = Theme.Card(todayStack);
        todayCard.Margin = new Thickness(0, 14, 0, 0);
        SetRow(todayCard, 1);
        right.Children.Add(todayCard);

        _systemSummary = Theme.Muted(string.Empty);
        _systemSummary.TextWrapping = TextWrapping.Wrap;
        _systemSummary.LineHeight = 18;
        var systemStack = new StackPanel();
        systemStack.Children.Add(Theme.Title("Erfasste Hardware"));
        systemStack.Children.Add(WithTopMargin(_systemSummary, 8));

        Border systemCard = Theme.Card(systemStack);
        systemCard.Margin = new Thickness(0, 14, 0, 0);
        SetRow(systemCard, 2);
        right.Children.Add(systemCard);

        SetColumn(right, 1);
        body.Children.Add(right);

        Children.Add(body);
    }

    public void ApplySettings(AppSettings settings) => _settings = settings;

    private UIElement BuildStatRow()
    {
        var grid = new Grid();

        StatCard[] cards = [_dailyAverageCard, _peakCard, _monthCard, _yearCard];

        for (int i = 0; i < cards.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            cards[i].Margin = new Thickness(i == 0 ? 0 : 7, 0, i == cards.Length - 1 ? 0 : 7, 0);
            SetColumn(cards[i], i);
            grid.Children.Add(cards[i]);
        }

        return grid;
    }

    public void UpdateLive(PowerSnapshot snapshot, IReadOnlyList<double> liveValues, string hardwareSummary)
    {
        _components.Update(snapshot);
        _liveChart.SetData(liveValues, Core.Monitoring.PowerMonitor.LiveWindow.TotalMinutes);

        string threshold = $"{_settings.Model.ReportingThresholdWatts:0.#} W";
        string below = snapshot.BelowThresholdWatts > 0
            ? $" Dazu {Formatting.Watts(snapshot.BelowThresholdWatts)} durch Kleinverbraucher unterhalb der Schwelle."
            : string.Empty;

        _componentsCaption.Text = $"Alle Verbraucher über {threshold}, absteigend sortiert.{below}";
        _systemSummary.Text = hardwareSummary;
    }

    public void UpdateStatistics(EnergyStatistics statistics)
    {
        string currency = _settings.CurrencySymbol;

        _dailyAverageCard.Update(
            Formatting.Watts(statistics.Today.AverageWatts),
            $"{Formatting.KilowattHours(statistics.Today.EnergyKilowattHours)} heute · {Formatting.Money(statistics.TodayCost, currency)}",
            statistics.Today.HasData
                ? $"Gemessen über {Formatting.Hours(statistics.Today.CoveredHours)} · Ø über alle Tage: {Formatting.KilowattHours(statistics.AverageDailyKilowattHours)} pro Tag"
                : "Noch keine Messwerte für heute.");

        string allTimePeak = statistics.AllTimePeak is { } peak
            ? $"Allzeit: {Formatting.Watts(peak.Watts)} am {peak.AtUtc.ToLocalTime():dd.MM.yyyy, HH:mm}"
            : "Allzeit: noch kein Wert";

        _peakCard.Update(
            Formatting.Watts(statistics.Today.PeakWatts),
            allTimePeak,
            "Höchster gemessener Momentanwert.");

        _monthCard.Update(
            Formatting.Watts(statistics.Month.AverageWatts),
            $"{Formatting.KilowattHours(statistics.Month.EnergyKilowattHours)} im {statistics.GeneratedAt:MMMM} · {Formatting.Money(statistics.MonthCost, currency)}",
            $"Hochrechnung Monatsende: {Formatting.KilowattHours(statistics.ProjectedMonthKilowattHours)} · {Formatting.Money(statistics.ProjectedMonthCost, currency)}");

        _yearCard.Update(
            Formatting.KilowattHours(statistics.ProjectedYearKilowattHours),
            $"{Formatting.Money(statistics.ProjectedYearCost, currency)} pro Jahr",
            Formatting.QualityLabel(statistics.Quality));

        _todaySummary.Text =
            $"Verbrauch: {Formatting.KilowattHours(statistics.Today.EnergyKilowattHours)}  ·  " +
            $"Kosten: {Formatting.Money(statistics.TodayCost, currency)}\n" +
            $"Ø Leistung: {Formatting.Watts(statistics.Today.AverageWatts)}  ·  " +
            $"Peak: {Formatting.Watts(statistics.Today.PeakWatts)}\n" +
            $"Aufzeichnungsdauer heute: {Formatting.Hours(statistics.Today.CoveredHours)}";
    }

    private static FrameworkElement WithTopMargin(FrameworkElement element, double margin)
    {
        element.Margin = new Thickness(0, margin, 0, 0);
        return element;
    }
}
