using System.Drawing;
using System.Windows.Forms;
using PowerPlugin.App.Ui;
using PowerPlugin.Core.Configuration;
using PowerPlugin.Core.Model;
using PowerPlugin.Core.Statistics;

namespace PowerPlugin.App.Tray;

/// <summary>
/// Owns the notification area icon: keeps the rendered value up to date, provides the context
/// menu and raises events for the actions the user picks.
/// </summary>
internal sealed class TrayController : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _currentValueItem;
    private readonly ToolStripMenuItem _autostartItem;

    private AppSettings _settings;
    private Icon? _currentIcon;
    private string _lastRenderedText = string.Empty;
    private Color _lastRenderedColor = Color.Empty;
    private bool _disposed;

    public TrayController(AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        _currentValueItem = new ToolStripMenuItem("Wird gemessen …") { Enabled = false };
        _autostartItem = new ToolStripMenuItem("Mit Windows starten") { CheckOnClick = true };
        _autostartItem.CheckedChanged += (_, _) => AutostartToggled?.Invoke(this, _autostartItem.Checked);

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Statistiken öffnen", null, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty))
        {
            Font = new Font(SystemFonts.MenuFont ?? SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold),
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_currentValueItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_autostartItem);
        menu.Items.Add(new ToolStripMenuItem("Beenden", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty)));

        _notifyIcon = new NotifyIcon
        {
            Text = "PowerPlugin",
            Visible = true,
            ContextMenuStrip = menu,
            Icon = AppIcon.LoadIcon() ?? SystemIcons.Application,
        };

        _notifyIcon.MouseClick += OnMouseClick;
        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? OpenRequested;

    public event EventHandler? ExitRequested;

    public event EventHandler<bool>? AutostartToggled;

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;

        // Force a redraw with the new thresholds or display mode.
        _lastRenderedText = string.Empty;
    }

    public void SetAutostartState(bool enabled)
    {
        if (_autostartItem.Checked != enabled)
        {
            // CheckedChanged would immediately write the value back; assigning the same value
            // is harmless because the handler is idempotent.
            _autostartItem.Checked = enabled;
        }
    }

    /// <summary>
    /// Refreshes icon and tooltip.
    /// </summary>
    /// <param name="snapshot">Latest sample, used for the per component breakdown in the tooltip.</param>
    /// <param name="statistics">Today's figures for the tooltip and the alternative display modes.</param>
    /// <param name="displayWatts">
    /// The value to show: the mean over the smoothing window rather than the latest sample, so the
    /// number stays readable at a refresh rate of twice per second.
    /// </param>
    public void Update(PowerSnapshot snapshot, EnergyStatistics statistics, double displayWatts)
    {
        if (_disposed)
        {
            return;
        }

        // Before the first sample arrives there is nothing to show; the application icon
        // stays in place rather than a misleading zero.
        if (displayWatts <= 0 && _settings.TrayDisplay == TrayDisplayMode.TotalWatts)
        {
            return;
        }

        string text = _settings.TrayDisplay switch
        {
            TrayDisplayMode.TodayKilowattHours => FormatCompact(statistics.Today.EnergyKilowattHours),
            TrayDisplayMode.TodayCost => FormatCompact((double)statistics.TodayCost),
            _ => Formatting.TrayWatts(displayWatts),
        };

        System.Windows.Media.Color themeColor = Theme.LoadColor(
            displayWatts, _settings.TrayGreenThresholdWatts, _settings.TrayAmberThresholdWatts);

        Color color = Color.FromArgb(themeColor.R, themeColor.G, themeColor.B);

        if (text != _lastRenderedText || color != _lastRenderedColor)
        {
            _lastRenderedText = text;
            _lastRenderedColor = color;

            Icon icon = TrayIconRenderer.Render(text, color, SystemInformation.SmallIconSize.Width);
            _notifyIcon.Icon = icon;

            // The previous icon must stay alive until the shell has picked up the new one.
            _currentIcon?.Dispose();
            _currentIcon = icon;
        }

        _notifyIcon.Text = BuildTooltip(snapshot, statistics, displayWatts);
        _currentValueItem.Text = $"Aktuell: {Formatting.Watts(displayWatts)}";
    }

    private string BuildTooltip(PowerSnapshot snapshot, EnergyStatistics statistics, double displayWatts)
    {
        var lines = new List<string>
        {
            $"PowerPlugin · {Formatting.Watts(displayWatts)}",
        };

        foreach (PowerComponent component in snapshot.Components.Take(3))
        {
            lines.Add($"{Shorten(component.Name, 22)}: {Formatting.Watts(component.Watts)}");
        }

        lines.Add($"Heute: {Formatting.KilowattHours(statistics.Today.EnergyKilowattHours)} · " +
                  $"{Formatting.Money(statistics.TodayCost, _settings.CurrencySymbol)}");

        // The shell truncates the tooltip at 127 characters.
        string tooltip = string.Join(Environment.NewLine, lines);
        return tooltip.Length <= 127 ? tooltip : tooltip[..127];
    }

    private static string Shorten(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";

    private static string FormatCompact(double value) => value switch
    {
        < 9.95 => $"{value:0.0}",
        < 999.5 => $"{value:0}",
        _ => $"{value / 1000.0:0.0}k",
    };

    private void OnMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            OpenRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Shows a balloon message, used for the "still running in the tray" hint.</summary>
    public void ShowMessage(string title, string message)
    {
        if (_disposed)
        {
            return;
        }

        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(4000);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Hiding first makes the icon disappear immediately instead of lingering as a ghost
        // until the user hovers over the notification area.
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _currentIcon?.Dispose();
    }
}
