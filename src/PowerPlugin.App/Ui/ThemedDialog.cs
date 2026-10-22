using System.Windows;
using System.Windows.Controls;

namespace PowerPlugin.App.Ui;

/// <summary>
/// A small modal window in the style of the app: dark title bar, a section heading, the content.
/// Used for the update offer and for "new in" after an update.
/// </summary>
internal sealed class ThemedDialog : Window
{
    public ThemedDialog(string heading, UIElement content, Window? owner)
    {
        Title = "PowerPlugin";
        Width = 500;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = owner is null || !owner.IsVisible;
        Background = Theme.BackgroundBrush;
        Icon = AppIcon.LoadImageSource(Core.Configuration.AppIconStyle.Subtle);

        if (owner is { IsVisible: true })
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        var stack = new StackPanel();
        stack.Children.Add(Theme.SectionHead(heading));

        var host = new Border { Margin = new Thickness(0, 14, 0, 0), Child = content };
        stack.Children.Add(host);

        Content = new Border { Padding = new Thickness(24, 20, 24, 22), Child = stack };
        DarkTitleBar.Attach(this);
    }

    /// <summary>After an update: what the new version brought, and where to see the code.</summary>
    public static ThemedDialog WhatsNew(Updates.InstalledUpdate update, Window? owner)
    {
        var body = new StackPanel();

        TextBlock intro = Theme.Body($"PowerPlugin ist von {update.From} auf {update.To} aktualisiert.");
        intro.TextWrapping = TextWrapping.Wrap;
        body.Children.Add(intro);

        if (update.Notes.Count > 0)
        {
            var notes = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };

            foreach (string note in update.Notes)
            {
                notes.Children.Add(UpdatePanel.Bullet(note));
            }

            body.Children.Add(notes);
        }

        body.Children.Add(UpdatePanel.ChangesLink(update.From, update.To));

        Button ok = Theme.Button("OK", primary: true);
        ok.HorizontalAlignment = HorizontalAlignment.Left;
        ok.Margin = new Thickness(0, 16, 0, 0);
        body.Children.Add(ok);

        var dialog = new ThemedDialog($"Neu in {update.To}", body, owner);
        ok.Click += (_, _) => dialog.Close();
        return dialog;
    }
}
