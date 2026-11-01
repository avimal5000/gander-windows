using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Gander.App.Hosting;
using Gander.App.Presentation;
using Gander.App.Presentation.Design;
using Gander.Core.Calibration;
using Shapes = System.Windows.Shapes;

namespace Gander.App.Windows;

/// <summary>
/// The pictures of the onboarding walkthrough, drawn with WPF shapes (vector, so they stay crisp at any DPI): the plate
/// frame with its italic caption, the looping scene, the three moves, the goose in a padlock and the tray strip. Colors
/// come from the theme and <see cref="GanderDesign"/>. The drawn pointer is only a shape. Animations run only while the
/// picture is on screen, and give way to a still frame when Windows animations are off.
/// </summary>
internal sealed class OnboardingArt
{
    /// <summary>Seconds of one pass of the scene before it loops.</summary>
    internal const double SceneSeconds = 10;

    private static readonly FontFamily Serif = new("Iowan Old Style, Palatino Linotype, Palatino, Book Antiqua, Georgia");

    private readonly GanderStyle _style;
    private readonly GanderTheme _theme;
    private readonly Brush _ink;
    private readonly Brush _soft;
    private readonly Brush _green;
    private readonly Brush _sage;
    private readonly Brush _paper;
    private readonly Brush _plate;
    private readonly Brush _frame;

    public OnboardingArt(GanderStyle style)
    {
        _style = style;
        _theme = style.Theme;
        var light = _theme.IsLight;
        _ink = style.Text;
        _soft = style.Secondary;
        _green = GanderStyle.Brush(_theme.Green);
        _sage = GanderStyle.Brush(GanderDesign.Sage);
        var ground = PaperColor(_theme);
        _paper = GanderStyle.Brush(ground);
        _plate = GanderStyle.Brush(light ? Rgba.Of(new Rgb(0xFF, 0xFF, 0xFF), 0.55).Over(ground) : _theme.Card.Over(ground));
        _frame = GanderStyle.Brush(light ? GanderDesign.Forest : GanderDesign.Sage);
    }

    /// <summary>The ivory paper of the walkthrough in light, charcoal in dark.</summary>
    public static Rgb PaperColor(GanderTheme theme) => theme.IsLight ? GanderDesign.Ivory : GanderDesign.Charcoal;

    /// <summary>One page: title, optional headline, the plate with its picture, and the page's own text.</summary>
    public FrameworkElement BuildPage(IntroPage page)
    {
        var panel = new StackPanel();
        var title = _style.Label(page.Title, GanderDesign.Type.Title);
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level1);
        panel.Children.Add(title);
        if (page.Headline is not null)
        {
            var headline = _style.Label(page.Headline, GanderDesign.Type.Title3);
            headline.Foreground = _green;
            headline.Margin = new Thickness(0, GanderDesign.Space.Sm, 0, 0);
            panel.Children.Add(headline);
        }

        FrameworkElement art;
        switch (page.Art)
        {
            case IntroArt.Scene:
                art = Scene();
                break;
            case IntroArt.Moves:
                art = Moves(page.Lines);
                break;
            case IntroArt.Privacy:
                art = Padlock();
                break;
            default:
                art = TrayStrip();
                break;
        }

        var plate = Plate(art, page.Figure, page.Caption);
        plate.Margin = new Thickness(0, 16, 0, 0);
        panel.Children.Add(plate);

        switch (page.Art)
        {
            case IntroArt.Privacy:
                var promises = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
                foreach (var line in page.Lines)
                {
                    promises.Children.Add(Bullet(line, GanderDesign.Type.Body));
                }

                panel.Children.Add(promises);
                break;
            case IntroArt.Tray:
                panel.Children.Add(TrayText(page.Lines));
                break;
            default:
                break;
        }

        return panel;
    }

    // ----- Plate: the double-ruled frame of the landing page, with "Fig. N" and an italic serif caption -----

    private StackPanel Plate(FrameworkElement art, string figure, string caption)
    {
        art.HorizontalAlignment = HorizontalAlignment.Center;
        var inner = new Grid();
        inner.Children.Add(art);
        inner.Children.Add(new Border
        {
            BorderBrush = _style.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Margin = new Thickness(-12),
            IsHitTestVisible = false,
        });
        var frame = new Border
        {
            Background = _plate,
            BorderBrush = _frame,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(GanderDesign.Radius.Card - 2),
            Padding = new Thickness(18),
            SnapsToDevicePixels = true,
            Child = inner,
        };

        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
        text.Inlines.Add(new Run(figure + "  ")
        {
            FontFamily = DesignFonts.Wpf(GanderDesign.Type.Caption),
            FontSize = GanderDesign.Type.Caption.Size,
            FontWeight = FontWeights.Bold,
            Foreground = _theme.IsLight ? _green : _sage,
        });
        text.Inlines.Add(new Run(caption)
        {
            FontFamily = Serif,
            FontStyle = FontStyles.Italic,
            FontSize = GanderDesign.Type.Body.Size,
            Foreground = _soft,
        });
        AutomationProperties.SetName(frame, figure + ". " + caption);

        var plate = new StackPanel();
        plate.Children.Add(frame);
        plate.Children.Add(text);
        return plate;
    }

    // ----- Fig. 1: the scene -----

    private FrameworkElement Scene()
    {
        var canvas = new Canvas { Width = 400, Height = 172, SnapsToDevicePixels = false };
        foreach (var x in new[] { 30.0, 220.0 })
        {
            canvas.Children.Add(Box(x, 14, 150, 92, 6, null, _ink, 2));
            canvas.Children.Add(Line(x + 75, 106, x + 75, 122, _ink, 2));
            canvas.Children.Add(Line(x + 52, 123, x + 98, 123, _ink, 2));
            for (var row = 0; row < 3; row++)
            {
                canvas.Children.Add(Box(x + 14, 62 + (row * 12), row == 2 ? 50 : 90, 4, 2, _soft, null, 0, 0.35));
            }
        }

        // The pointer sits on the left monitor and never moves: it is a shape.
        var pointer = Pointer(92, 36);
        canvas.Children.Add(pointer);

        // The shoulders, then the head (seen from behind) turning to the right.
        canvas.Children.Add(Stroke("M 170,171 A 30,24 0 0 1 230,171", _ink, 2.5));
        var head = new Canvas { Width = 400, Height = 172, IsHitTestVisible = false };
        var rotate = new RotateTransform(0, 200, 138);
        head.RenderTransform = rotate;
        head.Children.Add(Circle(200, 138, 12, _plate, _ink, 2.5));
        var profile = Stroke("M 211,133 L 217,135 L 211,139", _ink, 2);
        profile.Opacity = 0;
        head.Children.Add(profile);
        canvas.Children.Add(head);

        var gaze = Stroke("M 214,128 L 280,112", _sage, 2);
        gaze.StrokeDashArray = [3, 3];
        gaze.Opacity = 0;
        canvas.Children.Add(gaze);

        // The focus ring starts on the left monitor and slides to the right one.
        var ring = Box(24, 8, 162, 104, 10, null, _green, 3.5);
        var slide = new TranslateTransform();
        ring.RenderTransform = slide;
        canvas.Children.Add(ring);

        var typed = new TextBlock
        {
            FontFamily = DesignFonts.Wpf(GanderDesign.Mono),
            FontSize = 15,
            Foreground = _ink,
            Text = string.Empty,
        };
        Canvas.SetLeft(typed, 234);
        Canvas.SetTop(typed, 30);
        canvas.Children.Add(typed);

        void Still()
        {
            rotate.Angle = 25;
            profile.Opacity = 1;
            gaze.Opacity = 1;
            slide.X = 190;
            typed.Text = SetupPresentation.SceneTyped;
        }

        void Stop()
        {
            rotate.BeginAnimation(RotateTransform.AngleProperty, null);
            profile.BeginAnimation(UIElement.OpacityProperty, null);
            gaze.BeginAnimation(UIElement.OpacityProperty, null);
            slide.BeginAnimation(TranslateTransform.XProperty, null);
            typed.BeginAnimation(TextBlock.TextProperty, null);
        }

        void Start()
        {
            Stop();
            rotate.BeginAnimation(RotateTransform.AngleProperty, Track(0, (1.0, 0), (1.8, 25), (8.4, 25), (9.2, 0)));
            var fade = Track(0, (1.0, 0), (1.8, 1), (8.4, 1), (9.2, 0));
            profile.BeginAnimation(UIElement.OpacityProperty, fade);
            gaze.BeginAnimation(UIElement.OpacityProperty, fade);
            slide.BeginAnimation(TranslateTransform.XProperty, Track(0, (3.0, 0), (4.0, 190), (8.4, 190), (9.4, 0)));
            typed.BeginAnimation(TextBlock.TextProperty, Typing(SetupPresentation.SceneTyped));
        }

        canvas.IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true && AppTheme.Animate)
            {
                Start();
            }
            else
            {
                Stop();
                Still();
            }
        };
        Still();
        return canvas;
    }

    // Keyframes over the 10 second loop: each (seconds, value) is eased from the one before.
    private static DoubleAnimationUsingKeyFrames Track(double start, params (double Seconds, double Value)[] frames)
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(SceneSeconds),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(start, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        foreach (var (seconds, value) in frames)
        {
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(value, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds)), new CubicEase { EasingMode = EasingMode.EaseInOut }));
        }

        return animation;
    }

    private static StringAnimationUsingKeyFrames Typing(string text)
    {
        var animation = new StringAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(SceneSeconds),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        animation.KeyFrames.Add(new DiscreteStringKeyFrame(string.Empty, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        for (var count = 1; count <= text.Length; count++)
        {
            animation.KeyFrames.Add(new DiscreteStringKeyFrame(text[..count], KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.5 + (0.3 * (count - 1))))));
        }

        animation.KeyFrames.Add(new DiscreteStringKeyFrame(string.Empty, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(9.2))));
        return animation;
    }

    // ----- Fig. 2: look, settle, type -----

    private FrameworkElement Moves(System.Collections.Generic.IReadOnlyList<string> lines)
    {
        var grid = new Grid { Width = 408 };
        for (var i = 0; i < 3; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
        }

        for (var i = 0; i < lines.Count && i < 3; i++)
        {
            var column = new StackPanel { Margin = new Thickness(6, 0, 6, 0) };
            var picture = i switch
            {
                0 => LookPictogram(),
                1 => SettlePictogram(),
                _ => TypePictogram(),
            };
            picture.HorizontalAlignment = HorizontalAlignment.Center;
            var number = new TextBlock
            {
                Text = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                FontFamily = Serif,
                FontStyle = FontStyles.Italic,
                FontSize = GanderDesign.Type.Title3.Size,
                Foreground = _theme.IsLight ? _green : _sage,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 2),
            };
            var text = _style.Label(lines[i], GanderDesign.Type.Callout);
            text.TextAlignment = TextAlignment.Center;
            column.Children.Add(picture);
            column.Children.Add(number);
            column.Children.Add(text);
            Grid.SetColumn(column, i);
            grid.Children.Add(column);
        }

        return grid;
    }

    private FrameworkElement LookPictogram()
    {
        var c = new Canvas { Width = 110, Height = 76 };
        c.Children.Add(Stroke("M 30,76 A 25,20 0 0 1 80,76", _ink, 2.5));
        c.Children.Add(Circle(55, 52, 11, _plate, _ink, 2.5));
        c.Children.Add(Stroke("M 24,30 A 36,26 0 0 1 86,30", _green, 2.5));
        c.Children.Add(Polygon(_green, (86, 30), (79, 20), (92, 22)));
        return c;
    }

    private FrameworkElement SettlePictogram()
    {
        var c = new Canvas { Width = 110, Height = 76 };
        c.Children.Add(Box(6, 12, 40, 28, 3, null, _ink, 2));
        c.Children.Add(Box(64, 12, 40, 28, 3, null, _ink, 2));
        c.Children.Add(Box(60, 8, 48, 36, 5, null, _green, 3));
        for (var i = 0; i < 3; i++)
        {
            c.Children.Add(Circle(40 + (i * 15), 62, 3.5, i == 2 ? _green : _soft, null, 0));
        }

        return c;
    }

    private FrameworkElement TypePictogram()
    {
        var c = new Canvas { Width = 110, Height = 76 };
        c.Children.Add(Box(34, 8, 66, 44, 4, null, _ink, 2));
        c.Children.Add(Box(30, 4, 74, 52, 6, null, _green, 3));
        var typed = new TextBlock
        {
            Text = SetupPresentation.SceneTyped,
            FontFamily = DesignFonts.Wpf(GanderDesign.Mono),
            FontSize = 12,
            Foreground = _ink,
        };
        Canvas.SetLeft(typed, 42);
        Canvas.SetTop(typed, 20);
        c.Children.Add(typed);
        c.Children.Add(Pointer(8, 40));
        return c;
    }

    // ----- Fig. 3: the goose in a padlock -----

    private FrameworkElement Padlock()
    {
        var c = new Canvas { Width = 96, Height = 108 };
        var shackle = Stroke("M 24,50 V 34 A 24,24 0 0 1 72,34 V 50", _ink, 6);
        c.Children.Add(shackle);
        c.Children.Add(Box(8, 46, 80, 58, 12, _green, null, 0));
        var light = _theme.IsLight;
        var mark = GanderStyle.MakeMark(40, light ? GanderDesign.Ivory : GanderDesign.Charcoal, light ? GanderDesign.Sage : GanderDesign.Charcoal);
        Canvas.SetLeft(mark, 28);
        Canvas.SetTop(mark, 54);
        c.Children.Add(mark);
        return c;
    }

    // ----- Fig. 4: the taskbar tray -----

    private FrameworkElement TrayStrip()
    {
        var c = new Canvas { Width = 300, Height = 52 };
        var bar = Brush(0x20, 0x20, 0x20);
        var icon = Brush(0xB8, 0xB8, 0xB8);
        c.Children.Add(Box(0, 0, 300, 52, 8, bar, Brush(0x45, 0x45, 0x45), 1));
        c.Children.Add(Stroke("M 130,24 L 136,18 L 142,24", icon, 1.8));
        var goose = GanderStyle.MakeMark(20, GanderDesign.Ivory, GanderDesign.Sage);
        Canvas.SetLeft(goose, 155);
        Canvas.SetTop(goose, 16);
        c.Children.Add(goose);
        var pulse = Circle(165, 26, 16, null, _sage, 2);
        pulse.RenderTransformOrigin = new Point(0.5, 0.5);
        var scale = new ScaleTransform(1, 1);
        pulse.RenderTransform = scale;
        c.Children.Add(pulse);

        c.Children.Add(Stroke("M 196,30 A 8,8 0 0 1 212,30", icon, 1.8));
        c.Children.Add(Circle(204, 33, 1.6, icon, null, 0));
        c.Children.Add(Box(224, 24, 8, 8, 1.5, icon, null, 0));
        c.Children.Add(Stroke("M 232,22 L 238,19 V 37 L 232,34", icon, 1.8));
        var clock = new TextBlock { Text = "9:41", Foreground = icon, FontSize = 12, FontFamily = DesignFonts.Wpf(GanderDesign.Type.Caption) };
        Canvas.SetLeft(clock, 254);
        Canvas.SetTop(clock, 17);
        c.Children.Add(clock);

        void Still()
        {
            scale.ScaleX = 1;
            scale.ScaleY = 1;
            pulse.Opacity = 0.8;
        }

        void Stop()
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            pulse.BeginAnimation(UIElement.OpacityProperty, null);
        }

        c.IsVisibleChanged += (_, e) =>
        {
            Stop();
            if (e.NewValue is true && AppTheme.Animate)
            {
                DoubleAnimation Grow(double from, double to) => new(from, to, TimeSpan.FromSeconds(2.2))
                {
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut },
                };

                scale.BeginAnimation(ScaleTransform.ScaleXProperty, Grow(0.7, 1.35));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, Grow(0.7, 1.35));
                pulse.BeginAnimation(UIElement.OpacityProperty, Grow(0.9, 0));
            }
            else
            {
                Still();
            }
        };
        Still();
        return c;
    }

    private FrameworkElement TrayText(System.Collections.Generic.IReadOnlyList<string> lines)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        foreach (var line in lines)
        {
            panel.Children.Add(_style.Label(line, GanderDesign.Type.Body));
        }

        var heading = _style.Label(SetupPresentation.NextHeading, GanderDesign.Type.Headline);
        heading.Margin = new Thickness(0, 14, 0, 4);
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level2);
        panel.Children.Add(heading);
        foreach (var item in SetupPresentation.NextItems)
        {
            panel.Children.Add(Bullet(item, GanderDesign.Type.Body));
        }

        return panel;
    }

    private StackPanel Bullet(string text, TextStyle style)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        var dot = new Shapes.Ellipse { Width = 7, Height = 7, Fill = _theme.IsLight ? _green : _sage, Margin = new Thickness(2, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        var label = _style.Label(text, style);
        label.MaxWidth = 430;
        row.Children.Add(dot);
        row.Children.Add(label);
        return row;
    }

    // ----- Shape helpers -----

    private static SolidColorBrush Brush(byte r, byte g, byte b) => GanderStyle.Brush(new Rgb(r, g, b));

    private static Shapes.Ellipse Circle(double cx, double cy, double r, Brush? fill, Brush? stroke, double thickness)
    {
        var e = new Shapes.Ellipse { Width = r * 2, Height = r * 2, Fill = fill, Stroke = stroke, StrokeThickness = thickness };
        Canvas.SetLeft(e, cx - r);
        Canvas.SetTop(e, cy - r);
        return e;
    }

    private static Shapes.Rectangle Box(double x, double y, double w, double h, double radius, Brush? fill, Brush? stroke, double thickness, double opacity = 1)
    {
        var r = new Shapes.Rectangle { Width = w, Height = h, RadiusX = radius, RadiusY = radius, Fill = fill, Stroke = stroke, StrokeThickness = thickness, Opacity = opacity };
        Canvas.SetLeft(r, x);
        Canvas.SetTop(r, y);
        return r;
    }

    private static Shapes.Line Line(double x1, double y1, double x2, double y2, Brush stroke, double thickness) => new()
    {
        X1 = x1,
        Y1 = y1,
        X2 = x2,
        Y2 = y2,
        Stroke = stroke,
        StrokeThickness = thickness,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
    };

    private static Shapes.Path Stroke(string data, Brush stroke, double thickness) => new()
    {
        Data = Geometry.Parse(data),
        Stroke = stroke,
        StrokeThickness = thickness,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
    };

    private static Shapes.Polygon Polygon(Brush fill, params (double X, double Y)[] points)
    {
        var polygon = new Shapes.Polygon { Fill = fill };
        foreach (var (x, y) in points)
        {
            polygon.Points.Add(new Point(x, y));
        }

        return polygon;
    }

    // The mouse arrow as a drawing. It is a picture: nothing here reads or moves the real pointer.
    private Shapes.Polygon Pointer(double x, double y)
    {
        var arrow = Polygon(_ink, (0, 0), (0, 17), (4.5, 13), (7.5, 20), (10.5, 18.5), (7.5, 12), (13, 12));
        arrow.Stroke = _paper;
        arrow.StrokeThickness = 1.2;
        arrow.StrokeLineJoin = PenLineJoin.Round;
        Canvas.SetLeft(arrow, x);
        Canvas.SetTop(arrow, y);
        return arrow;
    }
}
