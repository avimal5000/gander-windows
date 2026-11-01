using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Gander.App.Hosting;
using Gander.App.Presentation;
using Gander.App.Presentation.Design;
using Gander.App.Tray;

namespace Gander.App.Windows;

/// <summary>
/// WPF's side of the design tokens: brushes, text, cards, buttons and switches built from <see cref="GanderTheme"/> and
/// <see cref="GanderDesign"/>, so no window picks a color, size or font by itself.
/// </summary>
public sealed class GanderStyle
{
    private const string Xmlns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

    /// <summary>Creates a style for <paramref name="theme"/>.</summary>
    public GanderStyle(GanderTheme theme)
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
        Amber = Brush(GanderDesign.Amber);
        Brick = Brush(GanderDesign.Brick);
    }

    /// <summary>The current theme and accent choice.</summary>
    public static GanderStyle Current() => new(AppTheme.Current);

    public GanderTheme Theme { get; }

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
        window.FontFamily = DesignFonts.Wpf(GanderDesign.Text);
        window.FontSize = GanderDesign.Type.Body.Size;
        window.UseLayoutRounding = true;
        WindowIcons.Apply(window);
    }

    /// <summary>
    /// A full-width row that goes somewhere: a card with a title, a line saying what is there, and a chevron. The card
    /// darkens a little on hover and when pressed; keyboard focus shows as an accent border.
    /// </summary>
    public Button MakeRowButton(string title, string help)
    {
        var text = new StackPanel();
        text.Children.Add(Label(title, GanderDesign.Type.Headline));
        var detail = Label(help, GanderDesign.Type.Caption, secondary: true);
        detail.Margin = new Thickness(0, GanderDesign.Space.Xs, 0, 0);
        text.Children.Add(detail);

        var chevron = Label("›", GanderDesign.Type.Title3Strong, secondary: true, wrap: false);
        chevron.VerticalAlignment = VerticalAlignment.Center;
        chevron.Margin = new Thickness(GanderDesign.Space.Xl, 0, 0, 0);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(chevron, 1);
        grid.Children.Add(text);
        grid.Children.Add(chevron);

        var button = new Button
        {
            Content = grid,
            Background = Card,
            BorderBrush = Line,
            Foreground = Text,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(GanderDesign.Space.Card + 2, GanderDesign.Space.Card, GanderDesign.Space.Card + 2, GanderDesign.Space.Card),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = help,
            Template = (ControlTemplate)XamlReader.Parse(
                $"<ControlTemplate {Xmlns} TargetType=\"Button\">"
                + $"<Border x:Name=\"B\" Background=\"{{TemplateBinding Background}}\" BorderBrush=\"{{TemplateBinding BorderBrush}}\" BorderThickness=\"{{TemplateBinding BorderThickness}}\" CornerRadius=\"{Num(GanderDesign.Radius.Card)}\" Padding=\"{{TemplateBinding Padding}}\">"
                + "<ContentPresenter HorizontalAlignment=\"{TemplateBinding HorizontalContentAlignment}\" VerticalAlignment=\"Center\"/></Border>"
                + "<ControlTemplate.Triggers>"
                + $"<Trigger Property=\"IsMouseOver\" Value=\"True\"><Setter TargetName=\"B\" Property=\"Background\" Value=\"{Hex(Theme.Hover.Over(Theme.Window))}\"/></Trigger>"
                + "<Trigger Property=\"IsPressed\" Value=\"True\"><Setter TargetName=\"B\" Property=\"Opacity\" Value=\"0.8\"/></Trigger>"
                + $"<Trigger Property=\"IsKeyboardFocused\" Value=\"True\"><Setter TargetName=\"B\" Property=\"BorderBrush\" Value=\"{Hex(Theme.Accent)}\"/></Trigger>"
                + "</ControlTemplate.Triggers></ControlTemplate>"),
        };
        System.Windows.Automation.AutomationProperties.SetName(button, title);
        System.Windows.Automation.AutomationProperties.SetHelpText(button, help);
        return button;
    }

    /// <summary>Colors and font of a page shown inside the Gander window.</summary>
    public void ApplyTo(UserControl page)
    {
        page.Background = Surface;
        page.Foreground = Text;
        page.FontFamily = DesignFonts.Wpf(GanderDesign.Text);
        page.FontSize = GanderDesign.Type.Body.Size;
        page.UseLayoutRounding = true;
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
        BorderThickness = new Thickness(GanderDesign.Space.Hairline),
        CornerRadius = new CornerRadius(GanderDesign.Radius.Card),
        Padding = new Thickness(GanderDesign.Space.Card),
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
                + $"<Border x:Name=\"B\" Background=\"{{TemplateBinding Background}}\" BorderBrush=\"{{TemplateBinding BorderBrush}}\" BorderThickness=\"{{TemplateBinding BorderThickness}}\" CornerRadius=\"{Num(GanderDesign.Radius.Chip)}\" Padding=\"{{TemplateBinding Padding}}\">"
                + "<ContentPresenter HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" RecognizesAccessKey=\"True\"/></Border>"
                + "<ControlTemplate.Triggers>"
                + "<Trigger Property=\"IsMouseOver\" Value=\"True\"><Setter TargetName=\"B\" Property=\"Opacity\" Value=\"0.85\"/></Trigger>"
                + "<Trigger Property=\"IsPressed\" Value=\"True\"><Setter TargetName=\"B\" Property=\"Opacity\" Value=\"0.7\"/></Trigger>"
                + "<Trigger Property=\"IsEnabled\" Value=\"False\"><Setter TargetName=\"B\" Property=\"Opacity\" Value=\"0.4\"/></Trigger>"
                + "</ControlTemplate.Triggers></ControlTemplate>"),
            BorderThickness = new Thickness(GanderDesign.Space.Hairline * 2),
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
            + $"<Border x:Name=\"S\" Background=\"Transparent\" CornerRadius=\"{Num(GanderDesign.Radius.Chip - 1)}\" Padding=\"14,4\" Margin=\"1\">"
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
        CornerRadius = new CornerRadius(GanderDesign.Radius.Chip),
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    /// <summary>The goose mark as WPF shapes: the body (with its eye cut out) in <paramref name="arc"/>, the wing in <paramref name="start"/> (the same color leaves no wing).</summary>
    public static FrameworkElement MakeMark(double side, Rgb arc, Rgb start, bool unitSquare = false)
    {
        var placement = unitSquare
            ? GanderMarkGeometry.UnitSquareIn(new MarkRect(0, 0, side, side))
            : GanderMarkGeometry.Fitting(new MarkRect(0, 0, side, side));
        var eye = placement.Point(GanderMarkGeometry.EyeCenter);
        var body = new GeometryGroup { FillRule = FillRule.EvenOdd };
        body.Children.Add(ToGeometry(GanderMarkGeometry.Silhouette, placement));
        body.Children.Add(new EllipseGeometry(new System.Windows.Point(eye.X, eye.Y), placement.Length(GanderMarkGeometry.EyeRadius), placement.Length(GanderMarkGeometry.EyeRadius)));

        var canvas = new Canvas { Width = side, Height = side };
        canvas.Children.Add(new System.Windows.Shapes.Path { Data = body, Fill = Brush(arc) });
        if (start != arc)
        {
            canvas.Children.Add(new System.Windows.Shapes.Path { Data = ToGeometry(GanderMarkGeometry.Wing, placement), Fill = Brush(start) });
        }

        return canvas;
    }

    private static PathGeometry ToGeometry(IReadOnlyList<MarkStep> steps, GanderMarkGeometry.Placement placement)
    {
        System.Windows.Point P(MarkPoint u)
        {
            var p = placement.Point(u);
            return new System.Windows.Point(p.X, p.Y);
        }

        var geometry = new PathGeometry();
        PathFigure? figure = null;
        foreach (var step in steps)
        {
            switch (step.Kind)
            {
                case MarkStepKind.Move:
                    figure = new PathFigure { StartPoint = P(step.A), IsClosed = true, IsFilled = true };
                    geometry.Figures.Add(figure);
                    break;
                case MarkStepKind.Line:
                    figure?.Segments.Add(new LineSegment(P(step.A), true));
                    break;
                case MarkStepKind.Cubic:
                    figure?.Segments.Add(new BezierSegment(P(step.A), P(step.B), P(step.C), true));
                    break;
                default:
                    break;
            }
        }

        return geometry;
    }

    private static string Hex(Rgb rgb) => $"#{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}";

    private static string Num(double value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>The window icon: the Mac's app icon from the embedded Gander.ico, drawn in code when it is not embedded.</summary>
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
            var frame = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/Gander.ico", UriKind.Absolute), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
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
