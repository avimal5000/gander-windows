using System;
using Sway.App.Presentation;
using Sway.App.Presentation.Design;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;
using Imaging = System.Drawing.Imaging;

namespace Sway.App.Tray;

/// <summary>
/// Draws the Swing mark with GDI+ from the Mac's geometry (<see cref="SwayMarkGeometry"/>, <see cref="TrayGlyphSpec"/>):
/// the tray glyph in all four states, the colored mark for headers, and the app icon tile.
/// </summary>
public static class MarkPainter
{
    /// <summary>Draws one tray glyph (the Mac's 18 point template image) into <paramref name="g"/>, scaled to <paramref name="size"/> pixels.</summary>
    public static void PaintGlyph(Drawing.Graphics g, int size, TrayIconAppearance look, Rgb ink)
    {
        Smooth(g);
        var k = size / TrayGlyphSpec.Side;
        var color = Drawing.Color.FromArgb(ink.R, ink.G, ink.B);

        if (look.Opacity >= 1)
        {
            PaintGlyphMark(g, k, look, color);
        }
        else
        {
            // The mark is one group, so a dimmed glyph has one even opacity where the shapes overlap.
            using var layer = new Drawing.Bitmap(size, size, Imaging.PixelFormat.Format32bppArgb);
            using (var lg = Drawing.Graphics.FromImage(layer))
            {
                Smooth(lg);
                PaintGlyphMark(lg, k, look, color);
            }

            var matrix = new Imaging.ColorMatrix { Matrix33 = (float)look.Opacity };
            using var attributes = new Imaging.ImageAttributes();
            attributes.SetColorMatrix(matrix);
            g.DrawImage(layer, new Drawing.Rectangle(0, 0, size, size), 0, 0, size, size, Drawing.GraphicsUnit.Pixel, attributes);
        }

        if (look.LockBadge)
        {
            PaintLock(g, k, color);
        }

        if (look.AttentionDot)
        {
            var c = TrayGlyphSpec.AlertCenter;
            CutOut(g, Disc(c.X * k, c.Y * k, (TrayGlyphSpec.AlertRadius + TrayGlyphSpec.AlertGap) * k));
            using var brush = new Drawing.SolidBrush(color);
            g.FillEllipse(brush, Box(c.X * k, c.Y * k, TrayGlyphSpec.AlertRadius * k));
        }
    }

    /// <summary>Draws the mark at unit-square proportions: the arc in one color, the small point and the large point in their own.</summary>
    public static void PaintMark(
        Drawing.Graphics g, SwayMarkGeometry.Placement placement, Drawing.Color arc, Drawing.Color start, Drawing.Color end)
    {
        Smooth(g);
        using (var pen = new Drawing.Pen(arc, (float)placement.Length(SwayMarkGeometry.ArcWidth)) { StartCap = Drawing2D.LineCap.Round, EndCap = Drawing2D.LineCap.Round })
        {
            g.DrawPath(pen, ArcPath(placement));
        }

        var s = placement.Point(SwayMarkGeometry.Start);
        var e = placement.Point(SwayMarkGeometry.End);
        using var startBrush = new Drawing.SolidBrush(start);
        using var endBrush = new Drawing.SolidBrush(end);
        g.FillEllipse(startBrush, Box(s.X, s.Y, placement.Length(SwayMarkGeometry.StartRadius)));
        g.FillEllipse(endBrush, Box(e.X, e.Y, placement.Length(SwayMarkGeometry.EndRadius)));
    }

    /// <summary>The app icon as the Mac draws it: an ivory arc on a forest rounded square, sage start point (the fallback when Sway.ico is not embedded).</summary>
    public static Drawing.Bitmap RenderAppIcon(int size)
    {
        var bitmap = new Drawing.Bitmap(size, size, Imaging.PixelFormat.Format32bppArgb);
        using var g = Drawing.Graphics.FromImage(bitmap);
        Smooth(g);
        var radius = (float)(size * SwayDesign.Radius.TileRatio);
        using (var path = RoundedRect(0, 0, size, size, radius))
        using (var brush = new Drawing.SolidBrush(ToColor(SwayDesign.Forest)))
        {
            g.FillPath(brush, path);
        }

        var placement = SwayMarkGeometry.UnitSquareIn(new MarkRect(0, 0, size, size));
        var ivory = ToColor(SwayDesign.Ivory);
        PaintMark(g, placement, ivory, ToColor(SwayDesign.Sage), ivory);
        return bitmap;
    }

    /// <summary>Converts a token color.</summary>
    public static Drawing.Color ToColor(Rgb rgb) => Drawing.Color.FromArgb(rgb.R, rgb.G, rgb.B);

    private static void PaintGlyphMark(Drawing.Graphics g, double k, TrayIconAppearance look, Drawing.Color color)
    {
        var placement = TrayGlyphSpec.Placement;
        using (var pen = new Drawing.Pen(color, (float)(TrayGlyphSpec.ArcWidth * k)) { StartCap = Drawing2D.LineCap.Round, EndCap = Drawing2D.LineCap.Round })
        {
            g.DrawPath(pen, ArcPath(Scaled(placement, k)));
        }

        using var brush = new Drawing.SolidBrush(color);
        var s = placement.Point(SwayMarkGeometry.Start);
        var e = placement.Point(SwayMarkGeometry.End);
        g.FillEllipse(brush, Box(s.X * k, s.Y * k, TrayGlyphSpec.StartRadius * k));
        if (look.HollowEnd)
        {
            // A ring whose outer edge is where the disc's edge would be; the arc stops at the ring.
            CutOut(g, Disc(e.X * k, e.Y * k, (TrayGlyphSpec.EndRadius - TrayGlyphSpec.RingWidth) * k));
            using var ring = new Drawing.Pen(color, (float)(TrayGlyphSpec.RingWidth * k));
            g.DrawEllipse(ring, Box(e.X * k, e.Y * k, (TrayGlyphSpec.EndRadius - TrayGlyphSpec.RingWidth / 2) * k));
        }
        else
        {
            g.FillEllipse(brush, Box(e.X * k, e.Y * k, TrayGlyphSpec.EndRadius * k));
        }
    }

    private static void PaintLock(Drawing.Graphics g, double k, Drawing.Color color)
    {
        var outline = TrayGlyphSpec.LockOutline;
        using (var cut = RoundedRect(outline.X * k, outline.Y * k, outline.Width * k, outline.Height * k, TrayGlyphSpec.LockOutlineRadius * k))
        {
            CutOut(g, cut);
        }

        var shackle = TrayGlyphSpec.Shackle;
        using (var arch = RoundedRect(shackle.X * k, shackle.Y * k, shackle.Width * k, shackle.Height * k, shackle.Width / 2 * k))
        using (var pen = new Drawing.Pen(color, (float)(TrayGlyphSpec.ShackleStroke * k)))
        {
            g.DrawPath(pen, arch);
        }

        var body = TrayGlyphSpec.LockBody;
        using var bodyPath = RoundedRect(body.X * k, body.Y * k, body.Width * k, body.Height * k, TrayGlyphSpec.LockBodyRadius * k);
        using var brush = new Drawing.SolidBrush(color);
        g.FillPath(brush, bodyPath);
    }

    private static SwayMarkGeometry.Placement Scaled(SwayMarkGeometry.Placement p, double k) =>
        new(p.Scale * k, new MarkPoint(p.Offset.X * k, p.Offset.Y * k));

    private static Drawing2D.GraphicsPath ArcPath(SwayMarkGeometry.Placement placement)
    {
        var (c1, c2) = SwayMarkGeometry.CubicControls();
        var a = placement.Point(SwayMarkGeometry.Start);
        var b = placement.Point(c1);
        var c = placement.Point(c2);
        var d = placement.Point(SwayMarkGeometry.End);
        var path = new Drawing2D.GraphicsPath();
        path.AddBezier((float)a.X, (float)a.Y, (float)b.X, (float)b.Y, (float)c.X, (float)c.Y, (float)d.X, (float)d.Y);
        return path;
    }

    /// <summary>Clears everything under <paramref name="path"/>, so a badge has a gap around it.</summary>
    private static void CutOut(Drawing.Graphics g, Drawing2D.GraphicsPath path)
    {
        var saved = g.CompositingMode;
        g.CompositingMode = Drawing2D.CompositingMode.SourceCopy;
        using var clear = new Drawing.SolidBrush(Drawing.Color.Transparent);
        g.FillPath(clear, path);
        g.CompositingMode = saved;
        path.Dispose();
    }

    private static Drawing2D.GraphicsPath Disc(double cx, double cy, double radius)
    {
        var path = new Drawing2D.GraphicsPath();
        path.AddEllipse(Box(cx, cy, radius));
        return path;
    }

    private static Drawing.RectangleF Box(double cx, double cy, double radius) =>
        new((float)(cx - radius), (float)(cy - radius), (float)(radius * 2), (float)(radius * 2));

    private static Drawing2D.GraphicsPath RoundedRect(double x, double y, double w, double h, double radius)
    {
        var d = (float)Math.Max(0.01, Math.Min(radius * 2, Math.Min(w, h)));
        var path = new Drawing2D.GraphicsPath();
        path.AddArc((float)x, (float)y, d, d, 180, 90);
        path.AddArc((float)(x + w - d), (float)y, d, d, 270, 90);
        path.AddArc((float)(x + w - d), (float)(y + h - d), d, d, 0, 90);
        path.AddArc((float)x, (float)(y + h - d), d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void Smooth(Drawing.Graphics g)
    {
        g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
        g.PixelOffsetMode = Drawing2D.PixelOffsetMode.HighQuality;
        g.CompositingQuality = Drawing2D.CompositingQuality.HighQuality;
    }
}
