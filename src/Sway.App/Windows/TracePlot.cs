using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Sway.App.Hosting;
using Sway.App.Presentation;
using Sway.App.Presentation.Design;

namespace Sway.App.Windows;

/// <summary>
/// A small time plot of one angle over the last 10 seconds: horizontal grid lines in degrees, a translucent band per
/// calibrated display in that display's color, and the live trail with its newest sample marked. Drawing only; the numbers
/// come from <see cref="PlotGeometry"/>.
/// </summary>
public sealed class TracePlot : FrameworkElement
{
    private const double LeftMargin = 42;
    private const double PlotMargin = 8;

    private readonly SwayStyle _style;
    private PlotData? _data;
    private string _emptyText = string.Empty;

    /// <summary>Creates a plot in the current look.</summary>
    public TracePlot(SwayStyle style)
    {
        _style = style;
        Height = 132;
        MinWidth = 200;
        SnapsToDevicePixels = true;
    }

    /// <summary>Shows new data and redraws. <paramref name="emptyText"/> is drawn over a plot with no samples.</summary>
    public void Show(PlotData data, string emptyText)
    {
        _data = data;
        _emptyText = emptyText;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext context)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        var theme = _style.Theme;
        var line = SwayStyle.Brush(theme.Line);
        var grid = new Pen(line, 1);
        var secondary = _style.Secondary;
        var plot = new Rect(LeftMargin, PlotMargin, Math.Max(10, width - LeftMargin - PlotMargin), Math.Max(10, height - (2 * PlotMargin)));
        context.DrawRectangle(null, new Pen(line, 1), plot);
        if (_data is not { } data)
        {
            return;
        }

        double Y(double value) => plot.Top + ((data.High - value) / (data.High - data.Low) * plot.Height);
        double X(double secondsAgo) => plot.Left + ((1 - (secondsAgo / PoseHistory.PlotWindow)) * plot.Width);

        // Grid and axis labels.
        var step = data.High - data.Low > 60 ? 20 : data.High - data.Low > 30 ? 10 : 5;
        for (var tick = Math.Ceiling(data.Low / step) * step; tick <= data.High; tick += step)
        {
            var y = Y(tick);
            context.DrawLine(tick == 0 ? new Pen(SwayStyle.Brush(new Rgba(theme.Text.R, theme.Text.G, theme.Text.B, 70)), 1) : grid, new Point(plot.Left, y), new Point(plot.Right, y));
            Text(context, string.Create(CultureInfo.InvariantCulture, $"{tick:0}°"), 11, secondary, new Point(plot.Left - 6, y), right: true);
        }

        // Bands, behind the trail.
        foreach (var band in data.Bands)
        {
            var color = SwayDesign.SeriesColor(band.SeriesIndex);
            var top = Math.Max(plot.Top, Y(band.High));
            var bottom = Math.Min(plot.Bottom, Y(band.Low));
            if (bottom <= top)
            {
                continue;
            }

            context.DrawRectangle(SwayStyle.Brush(Rgba.Of(color, 0.2)), null, new Rect(plot.Left, top, plot.Width, bottom - top));
            Text(context, band.Label, 11, SwayStyle.Brush(color), new Point(plot.Left + 6, top + 2), right: false);
        }

        // The trail.
        var trail = new Pen(_style.Green, 1.6) { LineJoin = PenLineJoin.Round };
        PlotPoint? newest = null;
        foreach (var segment in data.Segments)
        {
            if (segment.Count == 1)
            {
                context.DrawEllipse(_style.Green, null, new Point(X(segment[0].SecondsAgo), Y(segment[0].Value)), 1.6, 1.6);
            }
            else if (segment.Count > 1)
            {
                var geometry = new StreamGeometry();
                using (var g = geometry.Open())
                {
                    g.BeginFigure(new Point(X(segment[0].SecondsAgo), Y(segment[0].Value)), false, false);
                    for (var i = 1; i < segment.Count; i++)
                    {
                        g.LineTo(new Point(X(segment[i].SecondsAgo), Y(segment[i].Value)), true, true);
                    }
                }

                geometry.Freeze();
                context.DrawGeometry(null, trail, geometry);
            }

            if (segment.Count > 0)
            {
                newest = segment[^1];
            }
        }

        if (newest is { } last)
        {
            context.DrawEllipse(_style.Green, null, new Point(X(last.SecondsAgo), Y(last.Value)), 4, 4);
        }
        else
        {
            Text(context, _emptyText, 13, secondary, new Point(plot.Left + (plot.Width / 2), plot.Top + (plot.Height / 2) - 8), centered: true);
        }
    }

    private void Text(DrawingContext context, string text, double size, Brush brush, Point at, bool right = false, bool centered = false)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(DesignFonts.Wpf(SwayDesign.Text), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            size,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var origin = right ? new Point(at.X - formatted.Width, at.Y - (formatted.Height / 2))
            : centered ? new Point(at.X - (formatted.Width / 2), at.Y)
            : at;
        context.DrawText(formatted, origin);
    }
}
