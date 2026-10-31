using System;
using Sway.App.Hosting;
using Sway.App.Presentation;
using Sway.Core.Controller;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;

namespace Sway.App.Tray;

/// <summary>
/// Draws the Swing mark at runtime with GDI+: an arc swinging from a small sage dot to a larger dot. Solid when
/// tracking, a hollow dimmed ring when paused, a padlock badge for Focus Lock, a dot for Needs you. Nothing is written
/// to disk: the bitmap becomes an icon through its handle, which is copied and then destroyed.
/// </summary>
public static class TrayIconRenderer
{
    /// <summary>Draws one icon. The caller owns (and disposes) the result.</summary>
    public static Drawing.Icon Render(TrayIconState state, int size, bool lightTaskbar)
    {
        var look = TrayIconAppearance.For(state);
        var palette = TrayPalette.For(lightTaskbar);
        using var bitmap = new Drawing.Bitmap(size, size, Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Drawing.Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
            g.PixelOffsetMode = Drawing2D.PixelOffsetMode.HighQuality;
            g.Clear(Drawing.Color.Transparent);
            Draw(g, size, look, palette);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var wrapper = Drawing.Icon.FromHandle(handle);
            return (Drawing.Icon)wrapper.Clone();
        }
        finally
        {
            NativeShell.DestroyIcon(handle);
        }
    }

    private static void Draw(Drawing.Graphics g, int size, TrayIconAppearance look, TrayPalette palette)
    {
        var s = (float)size;
        var strength = look.Dimmed ? 110 : 255;
        var mark = Color(palette.Mark, strength);
        var sage = Color(palette.Accent, strength);

        var small = new Drawing.PointF(0.20f * s, 0.74f * s);
        var smallRadius = 0.09f * s;
        var large = new Drawing.PointF(0.78f * s, 0.74f * s);
        var largeRadius = 0.15f * s;
        var penWidth = Math.Max(1.5f, 0.09f * s);

        // The swing: the upper half of an ellipse from the small dot to the large one.
        var rx = (large.X - small.X) / 2;
        var ry = 0.50f * s;
        using (var pen = new Drawing.Pen(mark, penWidth) { StartCap = Drawing2D.LineCap.Round, EndCap = Drawing2D.LineCap.Round })
        {
            g.DrawArc(pen, small.X, small.Y - ry, rx * 2, ry * 2, 180, 180);
        }

        using (var brush = new Drawing.SolidBrush(sage))
        {
            g.FillEllipse(brush, small.X - smallRadius, small.Y - smallRadius, smallRadius * 2, smallRadius * 2);
        }

        var largeBox = new Drawing.RectangleF(large.X - largeRadius, large.Y - largeRadius, largeRadius * 2, largeRadius * 2);
        if (look.FilledEnd)
        {
            using var brush = new Drawing.SolidBrush(mark);
            g.FillEllipse(brush, largeBox);
        }
        else
        {
            var ringWidth = Math.Max(1.25f, 0.07f * s);
            largeBox.Inflate(-ringWidth / 2, -ringWidth / 2);
            using var ring = new Drawing.Pen(mark, ringWidth);
            g.DrawEllipse(ring, largeBox);
        }

        if (look.LockBadge)
        {
            DrawLock(g, s, mark, palette);
        }

        if (look.AttentionDot)
        {
            using var brush = new Drawing.SolidBrush(Color(palette.Attention, 255));
            var r = 0.15f * s;
            g.FillEllipse(brush, s - 2 * r - 0.02f * s, 0.02f * s, 2 * r, 2 * r);
        }
    }

    private static void DrawLock(Drawing.Graphics g, float s, Drawing.Color mark, TrayPalette palette)
    {
        var x = 0.70f * s;
        var y = 0.02f * s;
        var width = 0.28f * s;
        var bodyTop = y + 0.14f * s;
        var bodyHeight = 0.14f * s;
        var line = Math.Max(1f, 0.05f * s);

        using (var pen = new Drawing.Pen(mark, line))
        {
            var shackleWidth = width * 0.6f;
            g.DrawArc(pen, x + (width - shackleWidth) / 2, y, shackleWidth, 0.2f * s, 180, 180);
        }

        using var brush = new Drawing.SolidBrush(Color(palette.Accent, 255));
        using var outline = new Drawing.Pen(mark, line);
        g.FillRectangle(brush, x, bodyTop, width, bodyHeight);
        g.DrawRectangle(outline, x, bodyTop, width, bodyHeight);
    }

    private static Drawing.Color Color(Rgb rgb, int alpha) => Drawing.Color.FromArgb(alpha, rgb.R, rgb.G, rgb.B);
}
