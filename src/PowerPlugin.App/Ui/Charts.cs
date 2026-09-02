using System.Globalization;
using System.Windows;
using System.Windows.Media;
using PowerPlugin.Core.Storage;

namespace PowerPlugin.App.Ui;

/// <summary>
/// Live power over the last minutes, drawn as a filled line. Rendering directly instead of
/// building a visual tree per point keeps the update at two samples per second cheap.
/// </summary>
internal sealed class LivePowerChart : FrameworkElement
{
    private IReadOnlyList<double> _values = Array.Empty<double>();
    private double _windowMinutes = 10;

    public void SetData(IReadOnlyList<double> values, double windowMinutes)
    {
        _values = values;
        _windowMinutes = windowMinutes <= 0 ? 10 : windowMinutes;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context)
    {
        double width = ActualWidth;
        double height = ActualHeight;

        if (width <= 1 || height <= 1)
        {
            return;
        }

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        var axisPen = new Pen(new SolidColorBrush(Theme.BorderColor), 1);
        axisPen.Freeze();

        // Horizontal guide lines with labels.
        double maximum = Math.Max(10, _values.Count > 0 ? _values.Max() * 1.15 : 10);
        maximum = NiceCeiling(maximum);

        for (int i = 0; i <= 4; i++)
        {
            double y = height - (height * i / 4.0);
            context.DrawLine(axisPen, new Point(0, Snap(y)), new Point(width, Snap(y)));

            FormattedText label = CreateText($"{maximum * i / 4.0:0} W", 10, Theme.TextMuted, dpi);
            context.DrawText(label, new Point(2, Math.Min(height - label.Height, Math.Max(0, y - label.Height - 1))));
        }

        if (_values.Count < 2)
        {
            FormattedText hint = CreateText("Messung läuft …", 12, Theme.TextMuted, dpi);
            context.DrawText(hint, new Point((width - hint.Width) / 2, (height - hint.Height) / 2));
            return;
        }

        // The chart always spans the full time window so a short history does not stretch.
        int capacity = Math.Max(_values.Count, 2);
        var geometry = new StreamGeometry();

        using (StreamGeometryContext stream = geometry.Open())
        {
            Point First(int index) => new(
                width * index / (capacity - 1.0),
                height - (height * Math.Clamp(_values[index] / maximum, 0, 1)));

            stream.BeginFigure(new Point(First(0).X, height), isFilled: true, isClosed: true);
            stream.LineTo(First(0), isStroked: false, isSmoothJoin: false);

            for (int i = 1; i < _values.Count; i++)
            {
                stream.LineTo(First(i), isStroked: true, isSmoothJoin: true);
            }

            stream.LineTo(new Point(First(_values.Count - 1).X, height), isStroked: false, isSmoothJoin: false);
        }

        geometry.Freeze();

        var fill = new LinearGradientBrush(
            Color.FromArgb(0x66, Theme.Accent.R, Theme.Accent.G, Theme.Accent.B),
            Color.FromArgb(0x08, Theme.Accent.R, Theme.Accent.G, Theme.Accent.B),
            new Point(0, 0),
            new Point(0, 1));
        fill.Freeze();

        var stroke = new Pen(new SolidColorBrush(Theme.Accent), 1.6);
        stroke.Freeze();

        context.DrawGeometry(fill, stroke, geometry);

        FormattedText caption = CreateText($"letzte {_windowMinutes:0} Minuten", 10, Theme.TextMuted, dpi);
        context.DrawText(caption, new Point(width - caption.Width - 2, height - caption.Height - 1));
    }

    private static double NiceCeiling(double value)
    {
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(1, value))));
        return Math.Ceiling(value / (magnitude / 2)) * (magnitude / 2);
    }

    private static double Snap(double value) => Math.Round(value) + 0.5;

    internal static FormattedText CreateText(string text, double size, Color color, double pixelsPerDip) => new(
        text,
        CultureInfo.CurrentCulture,
        FlowDirection.LeftToRight,
        new Typeface(Theme.UiFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
        size,
        new SolidColorBrush(color),
        pixelsPerDip);
}

/// <summary>Daily consumption of the last 30 days as a bar chart.</summary>
internal sealed class DailyEnergyChart : FrameworkElement
{
    private IReadOnlyList<DailyEnergy> _days = Array.Empty<DailyEnergy>();

    public void SetData(IReadOnlyList<DailyEnergy> days)
    {
        _days = days;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context)
    {
        double width = ActualWidth;
        double height = ActualHeight;

        if (width <= 1 || height <= 1 || _days.Count == 0)
        {
            return;
        }

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        const double LabelHeight = 16;
        double plotHeight = Math.Max(10, height - LabelHeight);

        double maximum = _days.Max(d => d.EnergyKilowattHours);
        if (maximum <= 0)
        {
            FormattedText empty = LivePowerChart.CreateText(
                "Noch keine abgeschlossenen Tage aufgezeichnet.", 12, Theme.TextMuted, dpi);
            context.DrawText(empty, new Point((width - empty.Width) / 2, (height - empty.Height) / 2));
            return;
        }

        double slot = width / _days.Count;
        double barWidth = Math.Max(2, slot - 3);

        var todayBrush = new SolidColorBrush(Theme.Accent);
        todayBrush.Freeze();

        var normalBrush = new SolidColorBrush(Color.FromArgb(0xCC, 0x4C, 0x8D, 0xFF));
        normalBrush.Freeze();

        DateOnly today = DateOnly.FromDateTime(DateTime.Now);

        for (int i = 0; i < _days.Count; i++)
        {
            DailyEnergy day = _days[i];
            double barHeight = plotHeight * Math.Clamp(day.EnergyKilowattHours / maximum, 0, 1);

            var rectangle = new Rect(
                (i * slot) + ((slot - barWidth) / 2),
                plotHeight - barHeight,
                barWidth,
                Math.Max(1, barHeight));

            context.DrawRoundedRectangle(
                day.Day == today ? todayBrush : normalBrush, null, rectangle, 2, 2);

            // Only every seventh day gets a label so the axis stays readable.
            if (i % 7 == 0 || i == _days.Count - 1)
            {
                FormattedText label = LivePowerChart.CreateText(day.Day.ToString("dd.MM."), 10, Theme.TextMuted, dpi);
                double x = Math.Min(width - label.Width, Math.Max(0, rectangle.X + (barWidth / 2) - (label.Width / 2)));
                context.DrawText(label, new Point(x, plotHeight + 2));
            }
        }

        FormattedText scale = LivePowerChart.CreateText($"max. {maximum:0.00} kWh/Tag", 10, Theme.TextMuted, dpi);
        context.DrawText(scale, new Point(width - scale.Width - 2, 0));
    }
}
