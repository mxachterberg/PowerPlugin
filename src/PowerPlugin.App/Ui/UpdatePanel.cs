using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using PowerPlugin.App.Updates;
using PowerPlugin.Core.Monitoring;

namespace PowerPlugin.App.Ui;

/// <summary>
/// State, release notes, progress and buttons of an update - in the dialog that offers it and in
/// the settings, like the update panel of Clavichat.
/// </summary>
internal sealed class UpdatePanel : StackPanel
{
    private readonly bool _inDialog;

    /// <param name="inDialog">In the dialog the second button is "later", in the settings "check".</param>
    public UpdatePanel(bool inDialog)
    {
        _inDialog = inDialog;
    }

    public event EventHandler? InstallRequested;

    public event EventHandler? CheckRequested;

    public event EventHandler? LaterRequested;

    public void Render(UpdateState state)
    {
        Children.Clear();

        var latest = state.Latest;

        if (!_inDialog)
        {
            TextBlock installed = Theme.Value($"PowerPlugin {state.CurrentVersion}", 15);
            installed.Margin = new Thickness(0, 0, 0, 8);
            Add(installed);
        }

        Add(Line(Describe(state), state.Status is UpdateStatus.Available or UpdateStatus.Downloading or UpdateStatus.Installing));

        bool offering = latest is not null && state.Status is UpdateStatus.Available or UpdateStatus.Downloading or UpdateStatus.Error;

        if (offering && latest!.Notes.Count > 0)
        {
            var notes = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            foreach (string note in latest.Notes)
            {
                notes.Children.Add(Bullet(note));
            }

            Add(notes);
        }

        if (state.Status is UpdateStatus.Downloading or UpdateStatus.Installing)
        {
            Add(ProgressBar(state.Status == UpdateStatus.Installing ? 1 : state.Progress));
        }

        if (offering)
        {
            Add(ChangesLink(state.CurrentVersion, latest!.Version));
        }

        var actions = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };

        if (latest is not null && state.CanInstall && state.Status is UpdateStatus.Available or UpdateStatus.Error)
        {
            Button install = Theme.Button($"Jetzt aktualisieren ({FormatSize(latest!.Package.Size)})", primary: true);
            install.Margin = new Thickness(0, 0, 10, 0);
            install.Click += (_, _) => InstallRequested?.Invoke(this, EventArgs.Empty);
            actions.Children.Add(install);
        }

        if (_inDialog)
        {
            if (!state.IsBusy)
            {
                Button later = Theme.Button("Später");
                later.Click += (_, _) => LaterRequested?.Invoke(this, EventArgs.Empty);
                actions.Children.Add(later);
            }
        }
        else
        {
            Button check = Theme.Button("Nach Updates suchen");
            check.IsEnabled = !state.IsBusy;
            check.Click += (_, _) => CheckRequested?.Invoke(this, EventArgs.Empty);
            actions.Children.Add(check);
        }

        if (actions.Children.Count > 0)
        {
            Add(actions);
        }
    }

    private static string Describe(UpdateState state)
    {
        string? latest = state.Latest?.Version;

        return state.Status switch
        {
            UpdateStatus.Available when !state.CanInstall =>
                $"PowerPlugin {latest} ist da - dieser Entwicklungs-Build installiert es nicht selbst.",
            UpdateStatus.Available =>
                $"PowerPlugin {latest} ist da - du hast {state.CurrentVersion}.",
            UpdateStatus.Downloading =>
                $"PowerPlugin {latest} wird geladen … {Math.Floor(state.Progress * 100):0} %",
            UpdateStatus.Installing =>
                "Wird installiert - PowerPlugin startet gleich neu.",
            UpdateStatus.Checking =>
                "Suche nach Updates …",
            UpdateStatus.Current =>
                $"Du hast die neueste Version ({state.CurrentVersion}).{Checked(state)}",
            UpdateStatus.Error =>
                state.Error ?? "Die Suche nach Updates ist fehlgeschlagen.",
            UpdateStatus.Unsupported =>
                "Dieser Entwicklungs-Build (bin\\Debug, bin\\Release) installiert Updates nicht selbst - die installierte Fassung kann es.",
            _ =>
                "Noch nicht nach Updates gesucht.",
        };
    }

    private static string Checked(UpdateState state)
    {
        if (state.LastChecked is not { } when)
        {
            return string.Empty;
        }

        string day = when.Date == DateTimeOffset.Now.Date ? "heute" : when.ToString("dd.MM.", CultureInfo.CurrentCulture);
        return $" Zuletzt gesucht: {day} {when:HH:mm}.";
    }

    private void Add(UIElement element) => Children.Add(element);

    private static TextBlock Line(string text, bool emphasised)
    {
        TextBlock line = emphasised ? Theme.Body(text) : Theme.Muted(text);
        line.TextWrapping = TextWrapping.Wrap;
        line.TextTrimming = TextTrimming.None;
        return line;
    }

    /// <summary>One release note, with the accent square of the app as bullet.</summary>
    public static UIElement Bullet(string text)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(17) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(new Border
        {
            Width = 5,
            Height = 5,
            Background = Theme.AccentBrush,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 7, 0, 0),
        });

        TextBlock body = Theme.Body(text, 12.5);
        body.TextWrapping = TextWrapping.Wrap;
        body.TextTrimming = TextTrimming.None;
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);

        return grid;
    }

    private static UIElement ProgressBar(double fraction)
    {
        var fill = new Border { Background = Theme.AccentBrush, HorizontalAlignment = HorizontalAlignment.Left };
        var track = new Border
        {
            Background = Theme.BorderSoftBrush,
            Height = 4,
            Margin = new Thickness(0, 12, 0, 0),
            Child = fill,
        };
        track.SizeChanged += (_, _) => fill.Width = track.ActualWidth * Math.Clamp(fraction, 0, 1);
        return track;
    }

    /// <summary>The code changes between two versions on GitHub.</summary>
    public static UIElement ChangesLink(string? from, string to)
    {
        string url = from is null
            ? $"{UpdateService.RepositoryUrl}/releases/tag/v{to}"
            : $"{UpdateService.RepositoryUrl}/compare/v{from}...v{to}";

        var link = new Hyperlink(new Run("Code-Änderungen auf GitHub ansehen ↗"))
        {
            NavigateUri = new Uri(url),
            Foreground = Theme.AccentBrush,
            TextDecorations = null,
        };
        link.RequestNavigate += (_, e) => Open(e.Uri.AbsoluteUri);

        TextBlock text = Theme.Muted(string.Empty, 12);
        text.Inlines.Add(link);
        text.Margin = new Thickness(0, 10, 0, 0);
        return text;
    }

    private static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            DiagnosticsLog.Write("Link öffnen", exception);
        }
    }

    private static string FormatSize(long bytes) =>
        bytes >= 1024 * 1024
            ? string.Create(CultureInfo.CurrentCulture, $"{bytes / 1048576.0:0.0} MB")
            : string.Create(CultureInfo.CurrentCulture, $"{Math.Max(1, bytes / 1024)} KB");
}
