using System.Collections.Concurrent;
using System.Reflection;
using System.Windows.Media.Imaging;
using PowerPlugin.Core.Configuration;

namespace PowerPlugin.App;

/// <summary>
/// Loads the embedded application icon in the chosen variant. The tray icon itself is drawn at
/// runtime; this one is used for the window, the taskbar button, Alt+Tab and as the tray fallback
/// before the first measurement.
/// <para>
/// The icon of the executable in Explorer cannot follow the setting: it is compiled into the file
/// and always shows the default variant.
/// </para>
/// </summary>
internal static class AppIcon
{
    private static readonly ConcurrentDictionary<AppIconStyle, BitmapFrame?> ImageSources = new();
    private static readonly ConcurrentDictionary<AppIconStyle, System.Drawing.Icon?> Icons = new();

    public static BitmapFrame? LoadImageSource(AppIconStyle style) =>
        ImageSources.GetOrAdd(style, s =>
        {
            using Stream? stream = OpenResource(s);
            return stream is null ? null : BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        });

    public static System.Drawing.Icon? LoadIcon(AppIconStyle style) =>
        Icons.GetOrAdd(style, s =>
        {
            using Stream? stream = OpenResource(s);
            return stream is null ? null : new System.Drawing.Icon(stream);
        });

    private static Stream? OpenResource(AppIconStyle style) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(
            style == AppIconStyle.Bold ? "PowerPlugin-Bold.ico" : "PowerPlugin.ico");
}
