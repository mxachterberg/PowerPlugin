using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using PowerPlugin.Core.Configuration;
using PowerPlugin.Core.Hardware;
using PowerPlugin.Core.Model;
using PowerPlugin.Core.Statistics;

namespace PowerPlugin.App.Ui;

/// <summary>
/// The statistics window. It is created once and hidden instead of closed, so switching back
/// from the tray is instant and the charts keep their state.
/// </summary>
internal sealed class MainWindow : Window
{
    private readonly TextBlock _currentWatts;
    private readonly TextBlock _currentSubtitle;
    private readonly ContentControl _confidenceBadge = new();
    private readonly ContentControl _sourceBadge = new();
    private readonly Border _elevationBanner;
    private readonly TextBlock _statusText;

    private readonly OverviewPage _overview;
    private readonly HistoryPage _history;
    private readonly SettingsPage _settings;
    private readonly ContentControl _pageHost;
    private readonly List<ToggleNavButton> _navButtons = [];

    private readonly SolidColorBrush _currentWattsBrush = new(Theme.Text);

    private AppSettings _appSettings;
    private string _hardwareSummary = "Hardware wird erkannt …";

    public MainWindow(AppSettings settings)
    {
        _appSettings = settings;

        Title = "PowerPlugin - Stromverbrauch";
        Width = 1080;
        Height = 720;
        MinWidth = 900;
        MinHeight = 620;
        Background = Theme.BackgroundBrush;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = AppIcon.LoadImageSource();
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);

        _overview = new OverviewPage(settings);
        _history = new HistoryPage(settings);
        _settings = new SettingsPage(settings);

        _settings.SettingsSaved += (_, updated) => SettingsChanged?.Invoke(this, updated);
        _settings.ResetHistoryRequested += (_, _) => ResetHistoryRequested?.Invoke(this, EventArgs.Empty);
        _settings.OpenDataFolderRequested += (_, _) => OpenDataFolderRequested?.Invoke(this, EventArgs.Empty);
        _settings.RestartElevatedRequested += (_, _) => RestartElevatedRequested?.Invoke(this, EventArgs.Empty);

        _currentWatts = new TextBlock
        {
            Text = "--",
            FontFamily = Theme.DisplayFont,
            FontSize = 44,
            FontWeight = FontWeights.Bold,
            Foreground = _currentWattsBrush,
        };

        _currentSubtitle = Theme.Muted("Gesamtaufnahme des Systems");
        _confidenceBadge.Content = Theme.Badge("…", Theme.TextMuted);
        _sourceBadge.Content = Theme.Badge("…", Theme.TextMuted);

        _statusText = Theme.Muted(string.Empty, 10.5);
        _elevationBanner = BuildElevationBanner();

        _pageHost = new ContentControl { Content = _overview };

        Content = BuildLayout();
        SelectPage(0);

        SourceInitialized += (_, _) => ApplyDarkTitleBar();
        Closing += OnClosing;
    }

    public event EventHandler<AppSettings>? SettingsChanged;

    public event EventHandler? ResetHistoryRequested;

    public event EventHandler? OpenDataFolderRequested;

    public event EventHandler? RestartElevatedRequested;

    /// <summary>Raised when the user closes the window while "close to tray" is disabled.</summary>
    public event EventHandler? ExitRequested;

    private UIElement BuildLayout()
    {
        var root = new Grid { Margin = new Thickness(20, 16, 20, 14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // header
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // elevation banner
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // navigation
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // status

        root.Children.Add(BuildHeader());

        Grid.SetRow(_elevationBanner, 1);
        root.Children.Add(_elevationBanner);

        UIElement navigation = BuildNavigation();
        Grid.SetRow(navigation, 2);
        root.Children.Add(navigation);

        Grid.SetRow(_pageHost, 3);
        root.Children.Add(_pageHost);

        _statusText.Margin = new Thickness(2, 10, 0, 0);
        Grid.SetRow(_statusText, 4);
        root.Children.Add(_statusText);

        return root;
    }

    private UIElement BuildHeader()
    {
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new StackPanel { Orientation = Orientation.Horizontal };

        var valueStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        valueStack.Children.Add(_currentWatts);

        var badges = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        _sourceBadge.Margin = new Thickness(0, 0, 6, 0);
        badges.Children.Add(_sourceBadge);
        badges.Children.Add(_confidenceBadge);

        var subtitleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 8, 0, 0) };
        subtitleStack.Children.Add(_currentSubtitle);
        subtitleStack.Children.Add(badges);

        left.Children.Add(valueStack);
        left.Children.Add(subtitleStack);
        header.Children.Add(left);

        var titleStack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };

        TextBlock brand = Theme.Title("PowerPlugin", 17);
        brand.HorizontalAlignment = HorizontalAlignment.Right;
        titleStack.Children.Add(brand);

        TextBlock claim = Theme.Muted("Leistungsaufnahme und Energiestatistik");
        claim.HorizontalAlignment = HorizontalAlignment.Right;
        titleStack.Children.Add(claim);

        Grid.SetColumn(titleStack, 1);
        header.Children.Add(titleStack);

        return header;
    }

    private UIElement BuildNavigation()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 16, 0, 14),
        };

        string[] captions = ["Übersicht", "Verlauf", "Einstellungen"];

        for (int i = 0; i < captions.Length; i++)
        {
            int index = i;
            var button = new ToggleNavButton(captions[i]);
            button.Click += (_, _) => SelectPage(index);
            button.Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0);

            _navButtons.Add(button);
            panel.Children.Add(button);
        }

        return panel;
    }

    private Border BuildElevationBanner()
    {
        var text = Theme.Body(
            "Ohne Administratorrechte fehlen die Leistungssensoren der CPU. Die CPU-Leistung wird derzeit geschätzt.");
        text.TextWrapping = TextWrapping.Wrap;
        text.VerticalAlignment = VerticalAlignment.Center;

        Button restart = Theme.Button("Neu starten");
        restart.Margin = new Thickness(14, 0, 0, 0);
        restart.VerticalAlignment = VerticalAlignment.Center;
        restart.Click += (_, _) => RestartElevatedRequested?.Invoke(this, EventArgs.Empty);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(text);

        Grid.SetColumn(restart, 1);
        grid.Children.Add(restart);

        var background = new SolidColorBrush(Color.FromArgb(0x22, Theme.Warn.R, Theme.Warn.G, Theme.Warn.B));
        background.Freeze();

        var borderBrush = new SolidColorBrush(Color.FromArgb(0x66, Theme.Warn.R, Theme.Warn.G, Theme.Warn.B));
        borderBrush.Freeze();

        return new Border
        {
            Background = background,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 14, 0, 0),
            Visibility = Visibility.Collapsed,
            Child = grid,
        };
    }

    private void SelectPage(int index)
    {
        for (int i = 0; i < _navButtons.Count; i++)
        {
            _navButtons[i].SetActive(i == index);
        }

        _pageHost.Content = index switch
        {
            1 => _history,
            2 => _settings,
            _ => _overview,
        };
    }

    // ---- Updates -------------------------------------------------------------------

    public void ApplySettings(AppSettings settings)
    {
        _appSettings = settings;
        _overview.ApplySettings(settings);
        _history.ApplySettings(settings);
        _settings.Load(settings);
    }

    public void SetHardwareSummary(HardwareInventory inventory) =>
        _hardwareSummary = DescribeHardware(inventory);

    public void SetElevationState(bool isElevated, bool sensorsMissing)
    {
        _elevationBanner.Visibility = !isElevated && sensorsMissing ? Visibility.Visible : Visibility.Collapsed;
        _settings.SetElevationState(isElevated, sensorsMissing);
    }

    public void UpdateLive(PowerSnapshot snapshot, IReadOnlyList<double> liveValues)
    {
        Color color = Theme.LoadColor(
            snapshot.TotalWatts, _appSettings.TrayGreenThresholdWatts, _appSettings.TrayAmberThresholdWatts);

        _currentWatts.Text = Formatting.Watts(snapshot.TotalWatts);
        _currentWattsBrush.Color = color;

        _currentSubtitle.Text = snapshot.ConversionLossWatts > 0
            ? $"Gesamtaufnahme an der Steckdose · davon {Formatting.Watts(snapshot.ConversionLossWatts)} Netzteilverluste"
            : "Gesamtaufnahme des Systems";

        _confidenceBadge.Content = Theme.Badge(
            snapshot.Confidence.ToDisplayString(),
            snapshot.Confidence switch
            {
                MeasurementConfidence.High => Theme.Good,
                MeasurementConfidence.Medium => Theme.Accent,
                _ => Theme.Warn,
            });

        int measured = snapshot.Components.Count(c => c.Source.IsMeasured());
        _sourceBadge.Content = Theme.Badge(
            $"{measured} von {snapshot.Components.Length} per Sensor",
            measured > 0 ? Theme.Accent : Theme.TextMuted);

        _overview.UpdateLive(snapshot, liveValues, _hardwareSummary);

        _statusText.Text =
            $"Letzte Messung {snapshot.Timestamp:HH:mm:ss} · Intervall {_appSettings.SampleInterval.TotalSeconds:0.#} s · " +
            $"Daten in {AppPaths.DataDirectory}";
    }

    public void UpdateStatistics(EnergyStatistics statistics)
    {
        _overview.UpdateStatistics(statistics);
        _history.Update(statistics);
    }

    /// <summary>Brings a hidden or minimised window back to the foreground.</summary>
    public void ShowAndActivate()
    {
        Show();

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_appSettings.CloseToTray)
        {
            ExitRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Keep the process and the measurement running; the window only disappears.
        e.Cancel = true;
        Hide();
    }

    private static string DescribeHardware(HardwareInventory inventory)
    {
        var lines = new List<string>
        {
            $"{inventory.Cpu.Name} · {inventory.Cpu.PhysicalCores} Kerne / {inventory.Cpu.LogicalCores} Threads",
        };

        foreach (GpuInfo gpu in inventory.Gpus)
        {
            lines.Add($"{gpu.Name}{(gpu.IsIntegrated ? " (integriert)" : string.Empty)}");
        }

        if (inventory.MemoryModules.Count > 0)
        {
            double total = inventory.MemoryModules.Sum(m => m.CapacityGigabytes);
            lines.Add($"{inventory.MemoryModules.Count} Speichermodule, {total:0} GB gesamt");
        }
        else if (inventory.TotalMemoryGigabytes > 0)
        {
            lines.Add($"{inventory.TotalMemoryGigabytes:0} GB Arbeitsspeicher");
        }

        foreach (StorageDeviceInfo device in inventory.StorageDevices.Take(4))
        {
            lines.Add(device.Name);
        }

        if (inventory.StorageDevices.Count > 4)
        {
            lines.Add($"… und {inventory.StorageDevices.Count - 4} weitere Laufwerke");
        }

        if (inventory.MotherboardName is { Length: > 0 } board)
        {
            lines.Add(board);
        }

        lines.Add(inventory.IsMobileSystem ? "Mobiles System (Akkubetrieb möglich)" : "Desktop-System");

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Windows 11 keeps the title bar light unless the window opts into the dark variant.
    /// The call is best effort - on older builds the attribute is simply ignored.
    /// </summary>
    private void ApplyDarkTitleBar()
    {
        const int DwmwaUseImmersiveDarkMode = 20;

        try
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            int useDarkMode = 1;
            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref useDarkMode, sizeof(int));
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    /// <summary>Pill shaped navigation button with an active state.</summary>
    private sealed class ToggleNavButton : Button
    {
        private readonly SolidColorBrush _background = new(Theme.Surface);
        private readonly SolidColorBrush _foreground = new(Theme.TextMuted);

        public ToggleNavButton(string caption)
        {
            Content = caption;
            FontFamily = Theme.UiFont;
            FontSize = 13;
            Padding = new Thickness(18, 8, 18, 9);
            Background = _background;
            Foreground = _foreground;
            BorderBrush = Theme.BorderBrush;
            BorderThickness = new Thickness(1);
            Cursor = System.Windows.Input.Cursors.Hand;
            Template = BuildTemplate();
        }

        public void SetActive(bool active)
        {
            _background.Color = active ? Theme.Accent : Theme.Surface;
            _foreground.Color = active ? Colors.White : Theme.TextMuted;
            FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        }

        private static ControlTemplate BuildTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border), "Root");
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(BorderThicknessProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(18));

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);

            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };

            var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(OpacityProperty, 0.88, "Root"));
            template.Triggers.Add(hover);

            template.Seal();
            return template;
        }
    }
}
