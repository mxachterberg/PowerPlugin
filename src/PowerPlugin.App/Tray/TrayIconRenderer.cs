using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace PowerPlugin.App.Tray;

/// <summary>
/// Draws the current value straight into the notification area icon, so the taskbar shows a
/// readable number instead of a static logo.
/// </summary>
internal static class TrayIconRenderer
{
    /// <summary>Renders <paramref name="text"/> as white digits on a rounded coloured tile.</summary>
    /// <param name="text">One to three characters, e.g. "87" or "1.2k".</param>
    /// <param name="background">Tile colour, usually derived from the current load.</param>
    /// <param name="size">Edge length in pixels; the shell asks for 16 px at 100 % scaling.</param>
    public static Icon Render(string text, Color background, int size)
    {
        size = Math.Clamp(size, 16, 64);

        using var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

            // ClearType would bleed colour fringes onto the transparent corners.
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.Clear(Color.Transparent);

            // Square, like everything else in this design - the website contains exactly one
            // rounded corner in its whole stylesheet, and it belongs to a status dot.
            var bounds = new Rectangle(0, 0, size, size);
            using (var fill = new SolidBrush(background))
            {
                graphics.FillRectangle(fill, bounds);
            }

            DrawFittedText(graphics, text, bounds, Color.White);
        }

        return CreateIcon(bitmap);
    }

    /// <summary>
    /// Scales the font down until the text fits the tile. Three digits still have to be legible
    /// on a 16 pixel icon, so the search starts high and stops at a readable minimum.
    /// </summary>
    private static void DrawFittedText(Graphics graphics, string text, Rectangle bounds, Color color)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        double available = bounds.Width * 0.92;

        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap,
        };

        using var brush = new SolidBrush(color);

        // Deliberately not the monospace face of the rest of the interface: at sixteen pixels
        // across, three digits have to stay readable, and Segoe UI carries small sizes better.
        for (float emSize = bounds.Height * 0.82f; emSize >= 5f; emSize -= 0.5f)
        {
            using var font = new Font("Segoe UI", emSize, FontStyle.Bold, GraphicsUnit.Pixel);
            SizeF measured = graphics.MeasureString(text, font, int.MaxValue, format);

            if (measured.Width > available || measured.Height > bounds.Height)
            {
                continue;
            }

            var layout = new RectangleF(
                bounds.X,
                bounds.Y + ((bounds.Height - measured.Height) / 2f),
                bounds.Width,
                measured.Height);

            graphics.DrawString(text, font, brush, layout, format);
            return;
        }
    }

    /// <summary>
    /// Converts the bitmap into an icon that owns its data. <see cref="Icon.FromHandle"/> does not
    /// take ownership of the handle, so it is cloned and the original handle is released - without
    /// that, every update would leak a GDI handle.
    /// </summary>
    private static Icon CreateIcon(Bitmap bitmap)
    {
        IntPtr handle = bitmap.GetHicon();

        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
