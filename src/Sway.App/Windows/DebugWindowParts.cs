using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Sway.App.Presentation.Design;
using Sway.Core.Controller;

namespace Sway.App.Windows;

/// <summary>Small building blocks shared by the three debug windows, all drawn from the design tokens.</summary>
internal static class DebugWindowParts
{
    /// <summary>A section: a headline over a card holding <paramref name="body"/>.</summary>
    public static UIElement Section(this SwayStyle style, string? heading, UIElement body)
    {
        var panel = new StackPanel();
        if (heading is not null)
        {
            var title = style.Label(heading, SwayDesign.Type.Headline);
            title.Margin = new Thickness(0, 0, 0, SwayDesign.Space.Lg);
            panel.Children.Add(title);
        }

        panel.Children.Add(body);
        var card = style.MakeCard(panel);
        card.Margin = new Thickness(0, 0, 0, SwayDesign.Space.Section);
        return card;
    }

    /// <summary>A dot in the color of a status tone (Idle gray, Active green, Attention amber, Problem brick).</summary>
    public static Ellipse StatusDot(this SwayStyle style, StatusTone tone) => new()
    {
        Width = SwayDesign.Size.StatusDot,
        Height = SwayDesign.Size.StatusDot,
        Fill = tone switch
        {
            StatusTone.Active => style.Green,
            StatusTone.Attention => style.Amber,
            StatusTone.Problem => style.Brick,
            _ => style.Secondary,
        },
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, SwayDesign.Space.Lg, 0),
    };

    /// <summary>Copies text to the clipboard, only ever from a click. The clipboard can be locked by another app; then nothing happens.</summary>
    public static bool CopyToClipboard(string text)
    {
        try
        {
            Clipboard.SetDataObject(text, true);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>The window chrome all three share: scrolling content with the page padding, on the window surface.</summary>
    public static ScrollViewer Page(SwayStyle style, UIElement content)
    {
        var holder = new Border { Padding = new Thickness(SwayDesign.Space.SettingsPadding), Child = content, Background = style.Surface };
        return new ScrollViewer { Content = holder, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = style.Surface };
    }
}
