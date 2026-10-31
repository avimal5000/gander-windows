using System;
using System.Collections.Generic;
using Sway.App.Presentation;
using Sway.App.Presentation.Design;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;
using Imaging = System.Drawing.Imaging;

namespace Sway.App.Tray;

/// <summary>
/// Draws the goose mark with GDI+ from the Mac's geometry (<see cref="SwayMarkGeometry"/>, <see cref="TrayGlyphSpec"/>):
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

    /// <summary>Draws the goose at unit-square proportions: the body (with its eye cut out) in one color, the wing in another.</summary>
    public static void PaintMark(
        Drawing.Graphics g, SwayMarkGeometry.Placement placement, Drawing.Color body, Drawing.Color wing)
    {
        Smooth(g);
        using (var path = BodyPath(placement, placement.Length(SwayMarkGeometry.EyeRadius)))
        using (var brush = new Drawing.SolidBrush(body))
        {
            g.FillPath(brush, path);
        }

        if (wing != body)
        {
            using var wingPath = ToPath(SwayMarkGeometry.Wing, placement);
            using var brush = new Drawing.SolidBrush(wing);
            g.FillPath(brush, wingPath);
        }
    }

    /// <summary>The app icon as the Mac draws it: an ivory goose with a sage wing on a forest rounded square (the fallback when Gander.ico is not embedded).</summary>
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
        PaintMark(g, placement, ToColor(SwayDesign.Ivory), ToColor(SwayDesign.Sage));
        return bitmap;
    }

    /// <summary>Converts a token color.</summary>
    public static Drawing.Color ToColor(Rgb rgb) => Drawing.Color.FromArgb(rgb.R, rgb.G, rgb.B);

    private static void PaintGlyphMark(Drawing.Graphics g, double k, TrayIconAppearance look, Drawing.Color color)
    {
        var scaled = Scaled(TrayGlyphSpec.Placement, k);
        var eyeRadius = TrayGlyphSpec.EyeRadius * k;
        if (look.HollowEnd)
        {
            // The paused goose is an outline with its eye kept as a dot.
            using var outline = ToPath(SwayMarkGeometry.Silhouette, scaled);
            using var pen = new Drawing.Pen(color, (float)(TrayGlyphSpec.OutlineWidth * k)) { LineJoin = Drawing2D.LineJoin.Round };
            g.DrawPath(pen, outline);
            var eye = scaled.Point(SwayMarkGeometry.EyeCenter);
            using var dot = new Drawing.SolidBrush(color);
            g.FillEllipse(dot, Box(eye.X, eye.Y, eyeRadius));
        }
        else
        {
            using var body = BodyPath(scaled, eyeRadius);
            using var brush = new Drawing.SolidBrush(color);
            g.FillPath(brush, body);
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

    /// <summary>The outline as a GDI+ path.</summary>
    private static Drawing2D.GraphicsPath ToPath(IReadOnlyList<MarkStep> steps, SwayMarkGeometry.Placement placement)
    {
        var path = new Drawing2D.GraphicsPath(Drawing2D.FillMode.Alternate);
        MarkPoint current = default;
        foreach (var step in steps)
        {
            switch (step.Kind)
            {
                case MarkStepKind.Move:
                    path.StartFigure();
                    current = placement.Point(step.A);
                    break;
                case MarkStepKind.Line:
                    var line = placement.Point(step.A);
                    path.AddLine((float)current.X, (float)current.Y, (float)line.X, (float)line.Y);
                    current = line;
                    break;
                case MarkStepKind.Cubic:
                    var c1 = placement.Point(step.A);
                    var c2 = placement.Point(step.B);
                    var end = placement.Point(step.C);
                    path.AddBezier((float)current.X, (float)current.Y, (float)c1.X, (float)c1.Y, (float)c2.X, (float)c2.Y, (float)end.X, (float)end.Y);
                    current = end;
                    break;
                default:
                    path.CloseFigure();
                    break;
            }
        }

        return path;
    }

    /// <summary>The goose's outline with its eye as a hole (alternate fill).</summary>
    private static Drawing2D.GraphicsPath BodyPath(SwayMarkGeometry.Placement placement, double eyeRadius)
    {
        var path = ToPath(SwayMarkGeometry.Silhouette, placement);
        var eye = placement.Point(SwayMarkGeometry.EyeCenter);
        path.AddEllipse(Box(eye.X, eye.Y, eyeRadius));
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
