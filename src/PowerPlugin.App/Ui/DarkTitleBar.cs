using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace PowerPlugin.App.Ui;

/// <summary>
/// Paints the title bar of a window in the colours of the app instead of the system grey.
/// <para>
/// Windows 11 (build 22000 and later) accepts explicit caption, text and border colours.
/// Windows 10 rejects those attributes; there the dark title bar variant is the closest it gets,
/// so that is requested first and stays in place if the colour calls fail.
/// </para>
/// </summary>
internal static class DarkTitleBar
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    /// <summary>Applies the colours as soon as the window has a handle.</summary>
    public static void Attach(Window window) => window.SourceInitialized += (_, _) => Apply(window);

    private static void Apply(Window window)
    {
        try
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            int useDarkMode = 1;
            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref useDarkMode, sizeof(int));

            int caption = ToColorRef(Theme.Background);
            int text = ToColorRef(Theme.Text);
            int border = ToColorRef(Theme.BorderColor);

            DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref caption, sizeof(int));
            DwmSetWindowAttribute(handle, DwmwaTextColor, ref text, sizeof(int));
            DwmSetWindowAttribute(handle, DwmwaBorderColor, ref border, sizeof(int));
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    /// <summary>COLORREF is 0x00BBGGRR - blue in the high byte, red in the low one.</summary>
    private static int ToColorRef(Color color) => color.R | (color.G << 8) | (color.B << 16);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
