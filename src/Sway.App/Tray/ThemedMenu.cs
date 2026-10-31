using System;
using System.Runtime.InteropServices;
using Sway.App.Hosting;
using Sway.App.Presentation;
using Sway.App.Presentation.Design;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;
using Forms = System.Windows.Forms;

namespace Sway.App.Tray;

/// <summary>
/// Makes the tray's ContextMenuStrip look like the Mac popover with in-box code only: the shared design tokens for
/// color, type, radius and spacing, a rounded hover, an accent check mark, the mark beside the name, a colored status dot,
/// and (on Windows 11) rounded corners.
/// </summary>
internal static class ThemedMenu
{
    private const int CornerPreferenceAttribute = 33; // DWMWA_WINDOW_CORNER_PREFERENCE
    private const int RoundCorners = 2; // DWMWCP_ROUND

    /// <summary>Applies the theme. Called each time the menu opens, so a theme or accent change shows.</summary>
    public static void Apply(Forms.ContextMenuStrip menu, SwayTheme theme)
    {
        var palette = MenuPalette.For(theme);
        var scale = menu.DeviceDpi / 96f;
        menu.Renderer = new ThemedRenderer(palette, theme);
        menu.Font = DesignFonts.Gdi(SwayDesign.Type.Body, scale);
        menu.BackColor = ToColor(palette.Background);
        menu.ForeColor = ToColor(palette.Text);
        menu.ShowImageMargin = false;
        menu.ShowCheckMargin = true;
        menu.AutoSize = true;
        menu.Padding = new Forms.Padding(Scaled(SwayDesign.Space.Sm, scale));
        menu.MinimumSize = new Drawing.Size(Scaled(SwayDesign.Size.MenuWidth, scale), 0);
        if (menu.IsHandleCreated)
        {
            Round(menu.Handle);
        }
    }

    /// <summary>Sets the font and padding of one item from its role and the menu's DPI.</summary>
    public static void StyleItem(Forms.ToolStripItem item, TrayMenuEntry entry, float scale)
    {
        item.Tag = entry;
        var style = entry.Role switch
        {
            TrayEntryRole.Header => SwayDesign.Type.Headline,
            TrayEntryRole.Status or TrayEntryRole.Banner or TrayEntryRole.Look => SwayDesign.Type.Callout,
            _ => SwayDesign.Type.Body,
        };
        item.Font = DesignFonts.Gdi(style, scale);
        var vertical = entry.Role == TrayEntryRole.Banner ? SwayDesign.Space.Xs : SwayDesign.Space.Md - 1;
        item.Padding = new Forms.Padding(
            Scaled(SwayDesign.Space.Md, scale), Scaled(vertical, scale), Scaled(SwayDesign.Space.Md, scale), Scaled(vertical, scale));
        item.Margin = new Forms.Padding(Scaled(SwayDesign.Space.Xs, scale), 1, Scaled(SwayDesign.Space.Xs, scale), 1);
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

    private static int Scaled(double value, float scale) => (int)Math.Round(value * scale);

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
        private readonly SwayTheme _theme;

        public ThemedRenderer(MenuPalette palette, SwayTheme theme)
            : base(new ThemedColors(palette))
        {
            _palette = palette;
            _theme = theme;
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
            using var path = RoundedRectangle(bounds, (int)Math.Round(SwayDesign.Radius.Row * DpiOf(e.ToolStrip) / 96f));
            using var brush = new Drawing.SolidBrush(ToColor(_palette.Hover));
            e.Graphics.FillPath(brush, path);
        }

        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            var entry = e.Item.Tag as TrayMenuEntry;
            var secondary = entry?.Role is TrayEntryRole.Banner or TrayEntryRole.Look
                || (e.Item is Forms.ToolStripMenuItem { ShortcutKeyDisplayString: { Length: > 0 } shortcut } && e.Text == shortcut);
            e.TextColor = ToColor(secondary || !e.Item.Enabled && entry?.Role is not (TrayEntryRole.Header or TrayEntryRole.Status)
                ? _palette.Disabled
                : _palette.Text);
            e.Graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;

            var scale = DpiOf(e.ToolStrip) / 96f;
            switch (entry?.Role)
            {
                case TrayEntryRole.Header:
                    DrawHeaderMark(e, scale);
                    break;
                case TrayEntryRole.Status:
                    DrawStatusDot(e, entry, scale);
                    break;
            }

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
            using var pen = new Drawing.Pen(ToColor(e.Item.Enabled ? _theme.Accent : _palette.Disabled), Math.Max(1.5f, 1.75f * scale))
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

        // The Swing mark in Gander green sits in the check column, left of "Gander".
        private void DrawHeaderMark(Forms.ToolStripItemTextRenderEventArgs e, float scale)
        {
            var side = (float)(SwayDesign.Size.HeaderMark * scale);
            var x = e.TextRectangle.X - side - 6 * scale;
            var y = e.TextRectangle.Y + (e.TextRectangle.Height - side) / 2f;
            var placement = SwayMarkGeometry.Fitting(new MarkRect(x, y, side, side));
            var green = ToColor(_theme.Green);
            MarkPainter.PaintMark(e.Graphics, placement, green, green);
        }

        // A colored dot for the tone of the status line (Mac StatusBadge): forest, amber, brick, or secondary gray.
        private void DrawStatusDot(Forms.ToolStripItemTextRenderEventArgs e, TrayMenuEntry entry, float scale)
        {
            var diameter = (float)(SwayDesign.Size.StatusDot * scale);
            var x = e.TextRectangle.X - diameter - 8 * scale;
            var y = e.TextRectangle.Y + (e.TextRectangle.Height - diameter) / 2f;
            var color = entry.Tone switch
            {
                Sway.Core.Controller.StatusTone.Active => SwayDesign.Forest,
                Sway.Core.Controller.StatusTone.Attention => SwayDesign.Amber,
                Sway.Core.Controller.StatusTone.Problem => SwayDesign.Brick,
                _ => _theme.Flat(_theme.TextSecondary),
            };
            using var brush = new Drawing.SolidBrush(ToColor(color));
            e.Graphics.FillEllipse(brush, x, y, diameter, diameter);
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
