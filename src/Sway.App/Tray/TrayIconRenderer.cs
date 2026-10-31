using Sway.App.Hosting;
using Sway.App.Presentation;
using Sway.Core.Controller;
using Drawing = System.Drawing;

namespace Sway.App.Tray;

/// <summary>
/// Draws the tray icon at runtime with GDI+ from the Mac's glyph geometry (<see cref="MarkPainter"/>): solid when
/// tracking, dimmed with a hollow end point when paused, a padlock badge for Focus Lock, a dot for Needs you. One flat
/// color that follows the taskbar theme, like the Mac's template image. Nothing is written to disk: the bitmap becomes
/// an icon through its handle, which is copied and then destroyed.
/// </summary>
public static class TrayIconRenderer
{
    /// <summary>Draws one icon. The caller owns (and disposes) the result.</summary>
    public static Drawing.Icon Render(TrayIconState state, int size, bool lightTaskbar)
    {
        var look = TrayIconAppearance.For(state);
        var palette = TrayPalette.For(lightTaskbar);
        using var bitmap = new Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Drawing.Graphics.FromImage(bitmap))
        {
            g.Clear(Drawing.Color.Transparent);
            MarkPainter.PaintGlyph(g, size, look, palette.Mark);
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
}
