using System;
using System.Runtime.InteropServices;
using Sway.App.Presentation;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;
using Forms = System.Windows.Forms;

namespace Sway.App.Tray;

/// <summary>
/// Makes the tray's ContextMenuStrip look at home on Windows 11 with in-box code only: the app theme's colors, the
/// system menu font, DPI-scaled padding, a rounded hover, an accent check mark and (on Windows 11) rounded corners.
/// </summary>
internal static class ThemedMenu
{
    private const int CornerPreferenceAttribute = 33; // DWMWA_WINDOW_CORNER_PREFERENCE
    private const int RoundCorners = 2; // DWMWCP_ROUND

    /// <summary>Applies the palette for the current app theme. Called each time the menu opens, so a theme change shows.</summary>
    public static void Apply(Forms.ContextMenuStrip menu, bool lightApps)
    {
        var palette = MenuPalette.For(lightApps);
        menu.Renderer = new ThemedRenderer(palette);
        menu.Font = Drawing.SystemFonts.MenuFont ?? Drawing.SystemFonts.DefaultFont;
        menu.BackColor = ToColor(palette.Background);
        menu.ForeColor = ToColor(palette.Text);
        menu.ShowImageMargin = false;
        menu.ShowCheckMargin = true;
        menu.AutoSize = true;
        var scale = menu.DeviceDpi / 96f;
        menu.Padding = new Forms.Padding(Scaled(4, scale));
        menu.MinimumSize = new Drawing.Size(Scaled(220, scale), 0);
        if (menu.IsHandleCreated)
        {
            Round(menu.Handle);
        }
    }

    /// <summary>The padding of one item for the menu's DPI, so rows are comfortably tall and touch friendly.</summary>
    public static void PadItem(Forms.ToolStripItem item, float scale)
    {
        item.Padding = new Forms.Padding(Scaled(6, scale), Scaled(6, scale), Scaled(6, scale), Scaled(6, scale));
        item.Margin = new Forms.Padding(Scaled(2, scale), 1, Scaled(2, scale), 1);
    }

    /// <summary>Asks Windows 11 for rounded corners on the dropdown window. Older Windows ignores the call.</summary>
    public static void Round(IntPtr handle)
    {
        try
        {
            var preference = RoundCorners;
            _ = DwmSetWindowAttribute(handle, CornerPreferenceAttribute, ref preference, sizeof(int));
        }
        catch (Exception)
        {
            // Square corners are fine.
        }
    }

    internal static Drawing.Color ToColor(Rgb rgb) => Drawing.Color.FromArgb(rgb.R, rgb.G, rgb.B);

    private static int DpiOf(Forms.ToolStrip? strip) => strip?.DeviceDpi ?? 96;

    private static int Scaled(int value, float scale) => (int)Math.Round(value * scale);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private sealed class ThemedColors(MenuPalette palette) : Forms.ProfessionalColorTable
    {
        public override Drawing.Color ToolStripDropDownBackground => ToColor(palette.Background);

        public override Drawing.Color MenuBorder => ToColor(palette.Border);

        public override Drawing.Color MenuItemBorder => Drawing.Color.Transparent;

        public override Drawing.Color MenuItemSelected => ToColor(palette.Hover);

        public override Drawing.Color MenuItemSelectedGradientBegin => ToColor(palette.Hover);

        public override Drawing.Color MenuItemSelectedGradientEnd => ToColor(palette.Hover);

        public override Drawing.Color MenuItemPressedGradientBegin => ToColor(palette.Hover);

        public override Drawing.Color MenuItemPressedGradientMiddle => ToColor(palette.Hover);

        public override Drawing.Color MenuItemPressedGradientEnd => ToColor(palette.Hover);

        public override Drawing.Color ImageMarginGradientBegin => ToColor(palette.Background);

        public override Drawing.Color ImageMarginGradientMiddle => ToColor(palette.Background);

        public override Drawing.Color ImageMarginGradientEnd => ToColor(palette.Background);

        public override Drawing.Color SeparatorDark => ToColor(palette.Separator);

        public override Drawing.Color SeparatorLight => ToColor(palette.Background);
    }

    private sealed class ThemedRenderer : Forms.ToolStripProfessionalRenderer
    {
        private readonly MenuPalette _palette;

        public ThemedRenderer(MenuPalette palette)
            : base(new ThemedColors(palette))
        {
            _palette = palette;
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)
        {
            using var brush = new Drawing.SolidBrush(ToColor(_palette.Background));
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
        {
            using var pen = new Drawing.Pen(ToColor(_palette.Border));
            var bounds = e.AffectedBounds;
            e.Graphics.DrawRectangle(pen, 0, 0, bounds.Width - 1, bounds.Height - 1);
        }

        protected override void OnRenderImageMargin(Forms.ToolStripRenderEventArgs e)
        {
            // No separate margin column: the check sits on the same surface as the text.
        }

        protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled)
            {
                return;
            }

            var bounds = new Drawing.Rectangle(Drawing.Point.Empty, e.Item.Size);
            bounds.Inflate(-1, -1);
            e.Graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
            using var path = RoundedRectangle(bounds, (int)Math.Round(4 * DpiOf(e.ToolStrip) / 96f));
            using var brush = new Drawing.SolidBrush(ToColor(_palette.Hover));
            e.Graphics.FillPath(brush, path);
        }

        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = ToColor(e.Item.Enabled ? _palette.Text : _palette.Disabled);
            if (e.Item.Tag as string != "status")
            {
                base.OnRenderItemText(e);
                return;
            }

            // The status line at the top: bold, in the accent color.
            e.TextColor = ToColor(_palette.Accent);
            using var bold = new Drawing.Font(e.TextFont ?? e.Item.Font, Drawing.FontStyle.Bold);
            e.TextFont = bold;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e)
        {
            var scale = DpiOf(e.ToolStrip) / 96f;
            var box = e.ImageRectangle;
            var size = (int)Math.Round(9 * scale);
            var x = box.X + (box.Width - size) / 2;
            var y = box.Y + (box.Height - size) / 2;
            e.Graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
            using var pen = new Drawing.Pen(ToColor(e.Item.Enabled ? _palette.Accent : _palette.Disabled), Math.Max(1.5f, 1.75f * scale))
            {
                StartCap = Drawing2D.LineCap.Round,
                EndCap = Drawing2D.LineCap.Round,
                LineJoin = Drawing2D.LineJoin.Round,
            };
            e.Graphics.DrawLines(
                pen,
                [
                    new Drawing.PointF(x, y + size * 0.55f),
                    new Drawing.PointF(x + size * 0.38f, y + size * 0.95f),
                    new Drawing.PointF(x + size, y + size * 0.1f),
                ]);
        }

        protected override void OnRenderArrow(Forms.ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = ToColor(e.Item?.Enabled == false ? _palette.Disabled : _palette.Text);
            base.OnRenderArrow(e);
        }

        protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Drawing.Pen(ToColor(_palette.Separator));
            var y = e.Item.Height / 2;
            var inset = (int)Math.Round(8 * DpiOf(e.ToolStrip) / 96f);
            e.Graphics.DrawLine(pen, inset, y, e.Item.Width - inset, y);
        }

        private static Drawing2D.GraphicsPath RoundedRectangle(Drawing.Rectangle r, int radius)
        {
            var d = Math.Max(2, radius * 2);
            var path = new Drawing2D.GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
