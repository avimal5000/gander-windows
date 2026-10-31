using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sway.App.Hosting;
using Sway.App.Presentation;
using Sway.App.Presentation.Design;
using Sway.App.Tray;

namespace Sway.App.Windows;

/// <summary>
/// WPF's side of the design tokens: brushes, text, cards, buttons and switches built from <see cref="SwayTheme"/> and
/// <see cref="SwayDesign"/>, so no window picks a color, size or font by itself.
/// </summary>
public sealed class SwayStyle
{
    private const string Xmlns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

    /// <summary>Creates a style for <paramref name="theme"/>.</summary>
    public SwayStyle(SwayTheme theme)
    {
        Theme = theme;
        Surface = Brush(theme.Window);
        Card = Brush(theme.Card);
        Line = Brush(theme.Line);
        Text = Brush(theme.Text);
        Secondary = Brush(theme.TextSecondary);
        Accent = Brush(theme.Accent);
        OnAccent = Brush(theme.OnAccent);
        Green = Brush(theme.Green);
        Amber = Brush(SwayDesign.Amber);
        Brick = Brush(SwayDesign.Brick);
    }

    /// <summary>The current theme and accent choice.</summary>
    public static SwayStyle Current() => new(AppTheme.Current);

    public SwayTheme Theme { get; }

    public Brush Surface { get; }

    public Brush Card { get; }

    public Brush Line { get; }

    public Brush Text { get; }

    public Brush Secondary { get; }

    public Brush Accent { get; }

    public Brush OnAccent { get; }

    public Brush Green { get; }

    public Brush Amber { get; }

    public Brush Brick { get; }

    /// <summary>A frozen brush for a token color.</summary>
    public static SolidColorBrush Brush(Rgb rgb) => Brush(new Rgba(rgb.R, rgb.G, rgb.B));

    /// <summary>A frozen brush for a translucent token color.</summary>
    public static SolidColorBrush Brush(Rgba rgba)
    {
        var brush = new SolidColorBrush(Color.FromArgb(rgba.A, rgba.R, rgba.G, rgba.B));
        brush.Freeze();
        return brush;
    }

    /// <summary>A WPF color for a token color.</summary>
    public static Color ColorOf(Rgb rgb) => Color.FromRgb(rgb.R, rgb.G, rgb.B);

    /// <summary>Colors, font and icon of a window.</summary>
    public void ApplyTo(Window window)
    {
        window.Background = Surface;
        window.Foreground = Text;
        window.FontFamily = DesignFonts.Wpf(SwayDesign.Text);
        window.FontSize = SwayDesign.Type.Body.Size;
        window.UseLayoutRounding = true;
        WindowIcons.Apply(window);
    }

    /// <summary>Applies a type style to a text block.</summary>
    public TextBlock Label(string text, TextStyle style, bool secondary = false, bool wrap = true)
    {
        var block = new TextBlock
        {
            Text = text,
            FontFamily = DesignFonts.Wpf(style),
            FontSize = style.Size,
            FontWeight = DesignFonts.WpfWeight(style),
            Foreground = secondary ? Secondary : Text,
        };
        if (wrap)
        {
            block.TextWrapping = TextWrapping.Wrap;
        }

        return block;
    }

    /// <summary>Applies a type style to an existing text block.</summary>
    public void Apply(TextBlock block, TextStyle style, bool secondary = false)
    {
        block.FontFamily = DesignFonts.Wpf(style);
        block.FontSize = style.Size;
        block.FontWeight = DesignFonts.WpfWeight(style);
        block.Foreground = secondary ? Secondary : Text;
    }

    /// <summary>The Mac card: a soft rounded container with a hairline border.</summary>
    public Border MakeCard(UIElement child) => new()
    {
        Child = child,
        Background = Card,
        BorderBrush = Line,
        BorderThickness = new Thickness(SwayDesign.Space.Hairline),
        CornerRadius = new CornerRadius(SwayDesign.Radius.Card),
        Padding = new Thickness(SwayDesign.Space.Card),
    };

    /// <summary>A bordered button; <paramref name="prominent"/> fills it with the accent (the Mac's borderedProminent).</summary>
    public Button MakeButton(string text, bool prominent = false)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(14, 5, 14, 5),
            MinHeight = 28,
            Template = (ControlTemplate)XamlReader.Parse(
                $"<ControlTemplate {Xmlns} TargetType=\"Button\">"
                + $"<Border x:Name=\"B\" Background=\"{{TemplateBinding Background}}\" BorderBrush=\"{{TemplateBinding BorderBrush}}\" BorderThickness=\"{{TemplateBinding BorderThickness}}\" CornerRadius=\"{Num(SwayDesign.Radius.Chip)}\" Padding=\"{{TemplateBinding Padding}}\">"
                + "<ContentPresenter HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" RecognizesAccessKey=\"True\"/></Border>"
                + "<ControlTemplate.Triggers>"
                + "<Trigger Property=\"IsMouseOver\" Value=\"True\"><Setter TargetName=\"B\" Property=\"Opacity\" Value=\"0.85\"/></Trigger>"
                + "<Trigger Property=\"IsPressed\" Value=\"True\"><Setter TargetName=\"B\" Property=\"Opacity\" Value=\"0.7\"/></Trigger>"
                + "<Trigger Property=\"IsEnabled\" Value=\"False\"><Setter TargetName=\"B\" Property=\"Opacity\" Value=\"0.4\"/></Trigger>"
                + "</ControlTemplate.Triggers></ControlTemplate>"),
            BorderThickness = new Thickness(SwayDesign.Space.Hairline * 2),
        };
        if (prominent)
        {
            button.Background = Accent;
            button.BorderBrush = Accent;
            button.Foreground = OnAccent;
            button.FontWeight = FontWeights.Medium;
        }
        else
        {
            button.Background = Card;
            button.BorderBrush = Line;
            button.Foreground = Text;
        }

        return button;
    }

    /// <summary>A switch with its label on the left and the track on the right (the Mac's switch toggle style).</summary>
    public CheckBox MakeSwitch(string text) => new()
    {
        Content = text,
        Foreground = Text,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Template = (ControlTemplate)XamlReader.Parse(
            $"<ControlTemplate {Xmlns} TargetType=\"CheckBox\">"
            + "<Grid Background=\"Transparent\"><Grid.ColumnDefinitions><ColumnDefinition Width=\"*\"/><ColumnDefinition Width=\"Auto\"/></Grid.ColumnDefinitions>"
            + "<ContentPresenter VerticalAlignment=\"Center\" TextElement.Foreground=\"{TemplateBinding Foreground}\"/>"
            + $"<Border x:Name=\"Track\" Grid.Column=\"1\" Width=\"36\" Height=\"20\" CornerRadius=\"10\" Margin=\"12,0,0,0\" Background=\"{Hex(Theme.Flat(Theme.TextSecondary))}\">"
            + "<Ellipse x:Name=\"Thumb\" Width=\"14\" Height=\"14\" Margin=\"3\" Fill=\"#FFFFFF\" HorizontalAlignment=\"Left\"/></Border></Grid>"
            + "<ControlTemplate.Triggers>"
            + $"<Trigger Property=\"IsChecked\" Value=\"True\"><Setter TargetName=\"Track\" Property=\"Background\" Value=\"{Hex(Theme.Accent)}\"/><Setter TargetName=\"Thumb\" Property=\"HorizontalAlignment\" Value=\"Right\"/><Setter TargetName=\"Thumb\" Property=\"Fill\" Value=\"{Hex(Theme.OnAccent)}\"/></Trigger>"
            + "<Trigger Property=\"IsEnabled\" Value=\"False\"><Setter Property=\"Opacity\" Value=\"0.45\"/></Trigger>"
            + "</ControlTemplate.Triggers></ControlTemplate>"),
    };

    /// <summary>One segment of a segmented picker.</summary>
    public RadioButton MakeSegment(string text, string group) => new()
    {
        Content = text,
        GroupName = group,
        Foreground = Text,
        Template = (ControlTemplate)XamlReader.Parse(
            $"<ControlTemplate {Xmlns} TargetType=\"RadioButton\">"
            + $"<Border x:Name=\"S\" Background=\"Transparent\" CornerRadius=\"{Num(SwayDesign.Radius.Chip - 1)}\" Padding=\"14,4\" Margin=\"1\">"
            + "<ContentPresenter HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\"/></Border>"
            + "<ControlTemplate.Triggers>"
            + $"<Trigger Property=\"IsChecked\" Value=\"True\"><Setter TargetName=\"S\" Property=\"Background\" Value=\"{Hex(Theme.Accent)}\"/><Setter Property=\"Foreground\" Value=\"{Hex(Theme.OnAccent)}\"/></Trigger>"
            + "</ControlTemplate.Triggers></ControlTemplate>"),
    };

    /// <summary>The container of a segmented picker.</summary>
    public Border MakeSegmentHost(UIElement content) => new()
    {
        Child = content,
        Background = Brush(Theme.Line),
        CornerRadius = new CornerRadius(SwayDesign.Radius.Chip),
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    /// <summary>The Swing mark as WPF shapes, in one color or three, sized by <paramref name="side"/>.</summary>
    public static FrameworkElement MakeMark(double side, Rgb arc, Rgb start, Rgb end, bool unitSquare = false)
    {
        var placement = unitSquare
            ? SwayMarkGeometry.UnitSquareIn(new MarkRect(0, 0, side, side))
            : SwayMarkGeometry.Fitting(new MarkRect(0, 0, side, side));
        var (c1, c2) = SwayMarkGeometry.CubicControls();
        System.Windows.Point P(MarkPoint u)
        {
            var p = placement.Point(u);
            return new System.Windows.Point(p.X, p.Y);
        }

        var figure = new PathFigure { StartPoint = P(SwayMarkGeometry.Start), IsClosed = false };
        figure.Segments.Add(new BezierSegment(P(c1), P(c2), P(SwayMarkGeometry.End), true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        var canvas = new Canvas { Width = side, Height = side };
        canvas.Children.Add(new System.Windows.Shapes.Path
        {
            Data = geometry,
            Stroke = Brush(arc),
            StrokeThickness = placement.Length(SwayMarkGeometry.ArcWidth),
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        });
        AddDot(canvas, P(SwayMarkGeometry.Start), placement.Length(SwayMarkGeometry.StartRadius), start);
        AddDot(canvas, P(SwayMarkGeometry.End), placement.Length(SwayMarkGeometry.EndRadius), end);
        return canvas;
    }

    private static void AddDot(Canvas canvas, System.Windows.Point center, double radius, Rgb color)
    {
        var dot = new System.Windows.Shapes.Ellipse { Width = radius * 2, Height = radius * 2, Fill = Brush(color) };
        Canvas.SetLeft(dot, center.X - radius);
        Canvas.SetTop(dot, center.Y - radius);
        canvas.Children.Add(dot);
    }

    private static string Hex(Rgb rgb) => $"#{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}";

    private static string Num(double value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>The window icon: the Mac's app icon from the embedded Sway.ico, drawn in code when it is not embedded.</summary>
public static class WindowIcons
{
    private static ImageSource? _icon;

    /// <summary>Sets <paramref name="window"/>'s icon.</summary>
    public static void Apply(Window window)
    {
        _icon ??= Load() ?? Draw();
        if (_icon is not null)
        {
            window.Icon = _icon;
        }
    }

    private static ImageSource? Load()
    {
        try
        {
            var frame = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/Sway.ico", UriKind.Absolute), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            frame.Freeze();
            return frame;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static ImageSource? Draw()
    {
        try
        {
            using var bitmap = MarkPainter.RenderAppIcon(64);
            var handle = bitmap.GetHicon();
            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                NativeShell.DestroyIcon(handle);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }
}
