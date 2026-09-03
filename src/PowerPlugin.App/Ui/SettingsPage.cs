using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using PowerPlugin.Core.Configuration;

namespace PowerPlugin.App.Ui;

/// <summary>
/// Lets the user set the electricity price, the sampling rate and the coefficients of the
/// estimation model. Calibrating against a real wall socket meter happens here.
/// </summary>
internal sealed class SettingsPage : ScrollViewer
{
    private readonly TextBox _price;
    private readonly TextBox _currency;
    private readonly TextBox _interval;
    private readonly TextBox _psuEfficiency;
    private readonly TextBox _cpuTdp;
    private readonly TextBox _baseWatts;
    private readonly TextBox _threshold;
    private readonly TextBox _retention;
    private readonly TextBox _greenThreshold;
    private readonly TextBox _amberThreshold;
    private readonly TextBox _trayRefresh;
    private readonly TextBox _trayAverage;

    private readonly CheckBox _autostart;
    private readonly CheckBox _closeToTray;
    private readonly CheckBox _startMinimized;
    private readonly CheckBox _includeLosses;

    private readonly RadioButton _trayWatts;
    private readonly RadioButton _trayEnergy;
    private readonly RadioButton _trayCost;

    private readonly RadioButton _valueInstant;
    private readonly RadioButton _valueAverage;
    private readonly TextBlock _traySummary;

    private readonly TextBlock _status;
    private readonly TextBlock _elevationInfo;
    private readonly Button _elevateButton;

    private AppSettings _settings;

    public SettingsPage(AppSettings settings)
    {
        _settings = settings.Clone();

        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;

        _price = Theme.TextBox(string.Empty, 90);
        _currency = Theme.TextBox(string.Empty, 60);
        _interval = Theme.TextBox(string.Empty, 90);
        _psuEfficiency = Theme.TextBox(string.Empty, 90);
        _cpuTdp = Theme.TextBox(string.Empty, 90);
        _baseWatts = Theme.TextBox(string.Empty, 90);
        _threshold = Theme.TextBox(string.Empty, 90);
        _retention = Theme.TextBox(string.Empty, 90);
        _greenThreshold = Theme.TextBox(string.Empty, 90);
        _amberThreshold = Theme.TextBox(string.Empty, 90);
        _trayRefresh = Theme.TextBox(string.Empty, 90);
        _trayAverage = Theme.TextBox(string.Empty, 90);

        _autostart = Theme.CheckBox("Mit Windows starten", false);
        _closeToTray = Theme.CheckBox("Fenster schließen minimiert in den Infobereich", true);
        _startMinimized = Theme.CheckBox("Beim Start nur das Taskleistensymbol anzeigen", true);
        _includeLosses = Theme.CheckBox("Netzteilverluste einrechnen (Wert entspricht dann der Steckdose)", true);

        _trayWatts = Radio("Aktuelle Leistung in Watt", "TrayDisplay");
        _trayEnergy = Radio("Heutiger Verbrauch in kWh", "TrayDisplay");
        _trayCost = Radio("Heutige Kosten", "TrayDisplay");

        _valueInstant = Radio("Momentanwert - der zuletzt gemessene Wert, ungeglättet", "TrayValue");
        _valueAverage = Radio("Durchschnitt über das Zeitfenster unten", "TrayValue");

        _traySummary = Theme.Muted(string.Empty);
        _traySummary.TextWrapping = TextWrapping.Wrap;
        _traySummary.Margin = new Thickness(0, 10, 0, 0);

        _status = Theme.Muted(string.Empty);
        _status.Margin = new Thickness(0, 10, 0, 0);
        _status.TextWrapping = TextWrapping.Wrap;

        _elevationInfo = Theme.Muted(string.Empty);
        _elevationInfo.TextWrapping = TextWrapping.Wrap;
        _elevateButton = Theme.Button("Als Administrator neu starten");
        _elevateButton.Click += (_, _) => RestartElevatedRequested?.Invoke(this, EventArgs.Empty);

        Content = BuildContent();
        WireSummaryUpdates();
        Load(_settings);
    }

    public event EventHandler<AppSettings>? SettingsSaved;

    public event EventHandler? ResetHistoryRequested;

    public event EventHandler? OpenDataFolderRequested;

    public event EventHandler? RestartElevatedRequested;

    public void SetElevationState(bool isElevated, bool sensorsMissing)
    {
        _elevateButton.Visibility = isElevated ? Visibility.Collapsed : Visibility.Visible;

        _elevationInfo.Text = isElevated
            ? "Das Programm läuft mit Administratorrechten. Die Leistungssensoren von CPU und Mainboard sind vollständig verfügbar."
            : sensorsMissing
                ? "Ohne Administratorrechte lassen sich die Leistungsregister der CPU (Intel RAPL / AMD SMU) nicht auslesen. " +
                  "Die CPU-Leistung wird deshalb aus der Auslastung geschätzt. Ein Neustart mit erhöhten Rechten liefert echte Messwerte."
                : "Das Programm läuft ohne Administratorrechte. Alle benötigten Sensoren sind trotzdem verfügbar.";
    }

    public void Load(AppSettings settings)
    {
        _settings = settings.Clone();

        _price.Text = _settings.PricePerKilowattHour.ToString("0.####", CultureInfo.CurrentCulture);
        _currency.Text = _settings.CurrencySymbol;
        _interval.Text = _settings.SampleIntervalSeconds.ToString("0.#", CultureInfo.CurrentCulture);
        _psuEfficiency.Text = (_settings.Model.PowerSupplyEfficiency * 100).ToString("0.#", CultureInfo.CurrentCulture);
        _cpuTdp.Text = _settings.Model.CpuTdpWattsOverride?.ToString("0.#", CultureInfo.CurrentCulture) ?? string.Empty;
        _baseWatts.Text = _settings.Model.DesktopBaseWatts.ToString("0.#", CultureInfo.CurrentCulture);
        _threshold.Text = _settings.Model.ReportingThresholdWatts.ToString("0.##", CultureInfo.CurrentCulture);
        _retention.Text = _settings.HistoryRetentionDays.ToString(CultureInfo.CurrentCulture);
        _greenThreshold.Text = _settings.TrayGreenThresholdWatts.ToString("0", CultureInfo.CurrentCulture);
        _amberThreshold.Text = _settings.TrayAmberThresholdWatts.ToString("0", CultureInfo.CurrentCulture);
        _trayRefresh.Text = _settings.TrayRefreshSeconds.ToString("0.##", CultureInfo.CurrentCulture);
        _trayAverage.Text = _settings.TrayAverageWindowSeconds.ToString("0.#", CultureInfo.CurrentCulture);

        _autostart.IsChecked = _settings.StartWithWindows;
        _closeToTray.IsChecked = _settings.CloseToTray;
        _startMinimized.IsChecked = _settings.StartMinimized;
        _includeLosses.IsChecked = _settings.Model.IncludeConversionLosses;

        _trayWatts.IsChecked = _settings.TrayDisplay == TrayDisplayMode.TotalWatts;
        _trayEnergy.IsChecked = _settings.TrayDisplay == TrayDisplayMode.TodayKilowattHours;
        _trayCost.IsChecked = _settings.TrayDisplay == TrayDisplayMode.TodayCost;

        _valueInstant.IsChecked = _settings.TrayValue == TrayValueMode.Instantaneous;
        _valueAverage.IsChecked = _settings.TrayValue == TrayValueMode.Average;

        UpdateTraySummary();
    }

    /// <summary>
    /// Reads the current inputs back as a sentence, so the user can see what the combination of
    /// refresh rate, averaging window and sampling interval actually does before saving.
    /// </summary>
    private void UpdateTraySummary()
    {
        AppSettings preview = ReadInputs();

        _trayAverage.IsEnabled = preview.TrayValue == TrayValueMode.Average
            && preview.TrayDisplay == TrayDisplayMode.TotalWatts;

        _traySummary.Text = TrayDisplayDescription.Describe(preview);
    }

    /// <summary>Wires every input that feeds the summary to keep it live while typing.</summary>
    private void WireSummaryUpdates()
    {
        foreach (TextBox box in new[] { _interval, _trayRefresh, _trayAverage })
        {
            box.TextChanged += (_, _) => UpdateTraySummary();
        }

        foreach (RadioButton radio in new[] { _trayWatts, _trayEnergy, _trayCost, _valueInstant, _valueAverage })
        {
            radio.Checked += (_, _) => UpdateTraySummary();
        }
    }

    private UIElement BuildContent()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };

        // ---- Tariff and sampling ----------------------------------------------------
        var general = new Grid();
        AddRow(general, "Strompreis pro kWh", "Grundlage aller Kostenangaben und der Jahresprognose.", _price);
        AddRow(general, "Währungszeichen", "Wird hinter allen Kostenwerten angezeigt.", _currency);
        AddRow(general, "Messintervall (Sekunden)",
            "0,5 bis 60 Sekunden. Bestimmt, wie oft die Sensoren gelesen werden - und damit, wie oft " +
            "sich der Wert im Infobereich überhaupt ändern kann.", _interval);
        AddRow(general, "Verlauf aufbewahren (Tage)", "Ältere Messwerte werden beim Start gelöscht. 0 behält alles.", _retention);
        stack.Children.Add(Section("Tarif und Messung", general));

        // ---- Notification area -------------------------------------------------------
        var tray = new StackPanel();

        tray.Children.Add(Caption("Angezeigte Größe"));
        tray.Children.Add(_trayWatts);
        tray.Children.Add(_trayEnergy);
        tray.Children.Add(_trayCost);

        tray.Children.Add(Caption("Wie die Leistung dargestellt wird"));
        tray.Children.Add(_valueInstant);
        tray.Children.Add(_valueAverage);

        var trayTiming = new Grid();
        AddRow(trayTiming, "Mittelungsfenster (Sekunden)",
            "Länge des Zeitraums, über den gemittelt wird - zum Beispiel 3 oder 10 Sekunden. " +
            "Gilt nur für die Darstellung als Durchschnitt.", _trayAverage);
        AddRow(trayTiming, "Darstellungsintervall (Sekunden)",
            "Wie oft die Anzeige neu berechnet wird. Unabhängig vom Mittelungsfenster: " +
            "0,5 zeigt zweimal pro Sekunde einen neuen Wert, 5 nur alle fünf Sekunden.", _trayRefresh);
        AddRow(trayTiming, "Symbol grün bis (W)", "Bis zu diesem Wert wird das Symbol grün dargestellt.", _greenThreshold);
        AddRow(trayTiming, "Symbol gelb bis (W)", "Darüber wechselt das Symbol auf Rot.", _amberThreshold);
        tray.Children.Add(trayTiming);

        tray.Children.Add(_traySummary);

        stack.Children.Add(Section("Anzeige im Infobereich", tray));

        // ---- Behaviour --------------------------------------------------------------
        var behaviour = new StackPanel();
        behaviour.Children.Add(_autostart);
        behaviour.Children.Add(_closeToTray);
        behaviour.Children.Add(_startMinimized);

        stack.Children.Add(Section("Verhalten", behaviour));

        // ---- Model ------------------------------------------------------------------
        var model = new StackPanel();
        model.Children.Add(_includeLosses);

        var modelGrid = new Grid();
        AddRow(modelGrid, "Netzteil-Wirkungsgrad (%)",
            "80 Plus Bronze ≈ 85, Gold ≈ 90, Platinum ≈ 92. Bestimmt die eingerechneten Wandlungsverluste.", _psuEfficiency);
        AddRow(modelGrid, "CPU-TDP (W, optional)",
            "Nur nötig, wenn kein CPU-Leistungssensor verfügbar ist. Leer lassen für die automatische Schätzung.", _cpuTdp);
        AddRow(modelGrid, "Grundlast Mainboard (W)",
            "Chipsatz, Spannungswandler, USB, Audio und Netzwerk eines Desktop-Boards.", _baseWatts);
        AddRow(modelGrid, "Meldeschwelle (W)",
            "Verbraucher unterhalb dieser Grenze werden zusammengefasst statt einzeln aufgeführt.", _threshold);
        model.Children.Add(modelGrid);

        TextBlock calibration = Theme.Muted(
            "Kalibrierung: Wird der angezeigte Wert mit einem Steckdosen-Messgerät verglichen, lässt sich die " +
            "Abweichung über die Grundlast und den Wirkungsgrad ausgleichen. Komponenten mit echtem Sensor " +
            "bleiben davon unberührt.");
        calibration.TextWrapping = TextWrapping.Wrap;
        calibration.Margin = new Thickness(0, 8, 0, 0);
        model.Children.Add(calibration);

        stack.Children.Add(Section("Schätzmodell", model));

        // ---- Sensors ----------------------------------------------------------------
        var sensors = new StackPanel();
        sensors.Children.Add(_elevationInfo);
        _elevateButton.HorizontalAlignment = HorizontalAlignment.Left;
        _elevateButton.Margin = new Thickness(0, 10, 0, 0);
        sensors.Children.Add(_elevateButton);
        stack.Children.Add(Section("Sensorzugriff", sensors));

        // ---- Data --------------------------------------------------------------------
        var data = new StackPanel();

        TextBlock path = Theme.Muted($"Messwerte und Einstellungen liegen in:\n{AppPaths.DataDirectory}");
        path.TextWrapping = TextWrapping.Wrap;
        data.Children.Add(path);

        var dataButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };

        Button openFolder = Theme.Button("Datenordner öffnen");
        openFolder.Click += (_, _) => OpenDataFolderRequested?.Invoke(this, EventArgs.Empty);
        dataButtons.Children.Add(openFolder);

        Button reset = Theme.Button("Verlauf löschen");
        reset.Margin = new Thickness(10, 0, 0, 0);
        reset.Click += (_, _) => ResetHistoryRequested?.Invoke(this, EventArgs.Empty);
        dataButtons.Children.Add(reset);

        data.Children.Add(dataButtons);
        stack.Children.Add(Section("Daten", data));

        // ---- Save --------------------------------------------------------------------
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };

        Button save = Theme.Button("Einstellungen speichern", primary: true);
        save.Click += (_, _) => Save();
        actions.Children.Add(save);

        Button defaults = Theme.Button("Modell zurücksetzen");
        defaults.Margin = new Thickness(10, 0, 0, 0);
        defaults.Click += (_, _) =>
        {
            AppSettings reverted = _settings.Clone();
            reverted.Model = new Core.Estimation.PowerModelOptions();
            Load(reverted);
            _status.Text = "Die Standardwerte des Schätzmodells sind eingetragen. Zum Übernehmen speichern.";
        };
        actions.Children.Add(defaults);

        stack.Children.Add(actions);
        stack.Children.Add(_status);

        return stack;
    }

    private static TextBlock Caption(string text)
    {
        TextBlock caption = Theme.Muted(text);
        caption.Margin = new Thickness(0, 10, 0, 4);
        return caption;
    }

    private static Border Section(string title, UIElement content)
    {
        var stack = new StackPanel();
        stack.Children.Add(Theme.Title(title));

        var host = new Border { Margin = new Thickness(0, 10, 0, 0), Child = content };
        stack.Children.Add(host);

        Border card = Theme.Card(stack);
        card.Margin = new Thickness(0, 0, 0, 14);
        return card;
    }

    private static void AddRow(Grid grid, string label, string help, FrameworkElement input)
    {
        if (grid.ColumnDefinitions.Count == 0)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        int row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var text = new StackPanel { Margin = new Thickness(0, 6, 16, 6) };
        text.Children.Add(Theme.Body(label));

        TextBlock helpBlock = Theme.Muted(help, 10.5);
        helpBlock.TextWrapping = TextWrapping.Wrap;
        helpBlock.Margin = new Thickness(0, 2, 0, 0);
        text.Children.Add(helpBlock);

        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);

        input.VerticalAlignment = VerticalAlignment.Top;
        input.Margin = new Thickness(0, 6, 0, 6);
        Grid.SetRow(input, row);
        Grid.SetColumn(input, 1);
        grid.Children.Add(input);
    }

    private static RadioButton Radio(string caption, string groupName) => new()
    {
        Content = caption,
        FontFamily = Theme.UiFont,
        FontSize = 12.5,
        Foreground = Theme.TextBrush,
        Margin = new Thickness(0, 4, 0, 4),
        GroupName = groupName,
    };

    private void Save()
    {
        AppSettings updated = ReadInputs();

        _settings = updated;
        Load(updated);

        _status.Text = $"Gespeichert um {DateTime.Now:HH:mm:ss}. Die Änderungen sind sofort aktiv.";
        SettingsSaved?.Invoke(this, updated);
    }

    /// <summary>
    /// Parses every input into a settings object. Unreadable input keeps the stored value, so a
    /// half-typed number never destroys a setting.
    /// </summary>
    private AppSettings ReadInputs()
    {
        AppSettings updated = _settings.Clone();

        updated.PricePerKilowattHour = ParseDecimal(_price.Text, updated.PricePerKilowattHour, 0m, 10m);
        updated.CurrencySymbol = string.IsNullOrWhiteSpace(_currency.Text) ? "€" : _currency.Text.Trim();
        updated.SampleIntervalSeconds = ParseDouble(_interval.Text, updated.SampleIntervalSeconds, 0.5, 60);
        updated.HistoryRetentionDays = (int)ParseDouble(_retention.Text, updated.HistoryRetentionDays, 0, 3650);
        updated.TrayGreenThresholdWatts = ParseDouble(_greenThreshold.Text, updated.TrayGreenThresholdWatts, 1, 5000);
        updated.TrayAmberThresholdWatts = ParseDouble(_amberThreshold.Text, updated.TrayAmberThresholdWatts, 1, 5000);
        updated.TrayRefreshSeconds = ParseDouble(_trayRefresh.Text, updated.TrayRefreshSeconds, 0.1, 60);
        updated.TrayAverageWindowSeconds = ParseDouble(_trayAverage.Text, updated.TrayAverageWindowSeconds, 0.5, 600);
        updated.TrayValue = _valueInstant.IsChecked == true ? TrayValueMode.Instantaneous : TrayValueMode.Average;

        // A green limit above the amber limit would leave the amber band empty.
        if (updated.TrayAmberThresholdWatts <= updated.TrayGreenThresholdWatts)
        {
            updated.TrayAmberThresholdWatts = updated.TrayGreenThresholdWatts + 50;
        }

        updated.StartWithWindows = _autostart.IsChecked == true;
        updated.CloseToTray = _closeToTray.IsChecked == true;
        updated.StartMinimized = _startMinimized.IsChecked == true;

        updated.TrayDisplay = _trayEnergy.IsChecked == true
            ? TrayDisplayMode.TodayKilowattHours
            : _trayCost.IsChecked == true
                ? TrayDisplayMode.TodayCost
                : TrayDisplayMode.TotalWatts;

        updated.Model.IncludeConversionLosses = _includeLosses.IsChecked == true;
        updated.Model.PowerSupplyEfficiency =
            ParseDouble(_psuEfficiency.Text, updated.Model.PowerSupplyEfficiency * 100, 50, 100) / 100.0;
        updated.Model.DesktopBaseWatts = ParseDouble(_baseWatts.Text, updated.Model.DesktopBaseWatts, 0, 200);
        updated.Model.ReportingThresholdWatts = ParseDouble(_threshold.Text, updated.Model.ReportingThresholdWatts, 0, 100);

        // An empty field clears the override; unreadable input keeps the previous value.
        updated.Model.CpuTdpWattsOverride = string.IsNullOrWhiteSpace(_cpuTdp.Text)
            ? null
            : TryParseDouble(_cpuTdp.Text, 5, 500) ?? updated.Model.CpuTdpWattsOverride;

        return updated;
    }

    /// <summary>
    /// Accepts both the current culture's decimal separator and the invariant one, because
    /// values are often copied from data sheets that use a dot.
    /// </summary>
    private static double? TryParseDouble(string text, double minimum, double maximum)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value) ||
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return Math.Clamp(value, minimum, maximum);
        }

        return null;
    }

    private static double ParseDouble(string text, double fallback, double minimum, double maximum)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value) ||
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return Math.Clamp(value, minimum, maximum);
        }

        return fallback;
    }

    private static decimal ParseDecimal(string text, decimal fallback, decimal minimum, decimal maximum)
    {
        if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out decimal value) ||
            decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return Math.Clamp(value, minimum, maximum);
        }

        return fallback;
    }
}
