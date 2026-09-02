using System.Reflection;
using System.Windows.Media.Imaging;

namespace PowerPlugin.App;

/// <summary>
/// Loads the embedded application icon. The tray icon itself is drawn at runtime; this one is
/// used for the window, the task switcher and as a fallback before the first measurement.
/// </summary>
internal static class AppIcon
{
    private const string ResourceName = "PowerPlugin.ico";

    private static readonly Lazy<BitmapFrame?> ImageSourceLazy = new(LoadImageSourceCore, isThreadSafe: true);
    private static readonly Lazy<System.Drawing.Icon?> IconLazy = new(LoadIconCore, isThreadSafe: true);

    public static BitmapFrame? LoadImageSource() => ImageSourceLazy.Value;

    public static System.Drawing.Icon? LoadIcon() => IconLazy.Value;

    private static BitmapFrame? LoadImageSourceCore()
    {
        using Stream? stream = OpenResource();
        return stream is null ? null : BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
    }

    private static System.Drawing.Icon? LoadIconCore()
    {
        using Stream? stream = OpenResource();
        return stream is null ? null : new System.Drawing.Icon(stream);
    }

    private static Stream? OpenResource() =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
}
