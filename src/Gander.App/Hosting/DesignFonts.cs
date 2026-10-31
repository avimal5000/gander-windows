using System;
using System.Linq;
using System.Windows.Media;
using Gander.App.Presentation.Design;
using Drawing = System.Drawing;

namespace Gander.App.Hosting;

/// <summary>Turns the design tokens' type styles into WPF and GDI+ fonts. Segoe UI Variable first, Segoe UI as fallback.</summary>
public static class DesignFonts
{
    private static readonly Lazy<string[]> Installed = new(() =>
    {
        try
        {
            using var collection = new Drawing.Text.InstalledFontCollection();
            return collection.Families.Select(f => f.Name).ToArray();
        }
        catch (Exception)
        {
            return [];
        }
    });

    /// <summary>The WPF font family for a stack (WPF walks the comma list).</summary>
    public static FontFamily Wpf(FontStack stack) => new(stack.Wpf);

    /// <summary>The WPF family for a style.</summary>
    public static FontFamily Wpf(TextStyle style) => Wpf(GanderDesign.Type.StackFor(style));

    /// <summary>The WPF weight for a style.</summary>
    public static System.Windows.FontWeight WpfWeight(TextStyle style) => System.Windows.FontWeight.FromOpenTypeWeight(style.Weight);

    /// <summary>A GDI+ font for a style at a DPI scale (1 at 96 dpi). Pixels, so the size matches WPF.</summary>
    public static Drawing.Font Gdi(TextStyle style, float scale)
    {
        var name = GanderDesign.Type.StackFor(style).Resolve(IsInstalled);
        var bold = style.Weight >= 600 ? Drawing.FontStyle.Bold : Drawing.FontStyle.Regular;
        return new Drawing.Font(name, (float)style.Size * scale, bold, Drawing.GraphicsUnit.Pixel);
    }

    /// <summary>The first installed name of a stack, for text that must name one family.</summary>
    public static string ResolveName(FontStack stack) => stack.Resolve(IsInstalled);

    private static bool IsInstalled(string name) =>
        Installed.Value.Contains(name, StringComparer.OrdinalIgnoreCase);
}
