using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Sway.App.Presentation;
using Sway.App.Presentation.Design;

namespace Sway.App.Windows;

/// <summary>
/// A borderless, topmost window over one monitor showing a large "Look here" marker. It never takes focus and clicks go
/// through it (WS_EX_TRANSPARENT, NOACTIVATE, TOOLWINDOW); it never touches the pointer.
/// </summary>
public sealed class PromptOverlayWindow : Window
{
    private readonly Ellipse _ring = new();
    private readonly TextBlock _number = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _label = new() { Text = "Look here", HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Grid _marker = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0 };
    private int _x;
    private int _y;
    private int _width;
    private int _height;

    /// <summary>Creates the (hidden) overlay.</summary>
    public PromptOverlayWindow()
    {
        OverlayNative.Configure(this, "Sway prompt");
        var style = SwayStyle.Current();
        var green = style.Theme.Green;
        _ring.Stroke = SwayStyle.Brush(green);
        _ring.Fill = SwayStyle.Brush(Rgba.Of(SwayDesign.Ivory, 0.8));
        style.Apply(_number, SwayDesign.Type.Title3Strong);
        style.Apply(_label, SwayDesign.Type.Title3Strong);
        _number.Foreground = SwayStyle.Brush(green);
        _label.Foreground = SwayStyle.Brush(green);

        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var ringHolder = new Grid();
        ringHolder.Children.Add(_ring);
        ringHolder.Children.Add(_number);
        _number.VerticalAlignment = VerticalAlignment.Center;
        stack.Children.Add(ringHolder);
        stack.Children.Add(_label);
        _marker.Children.Add(stack);
        Content = new Grid { Children = { _marker } };
    }

    /// <summary>Puts the overlay over a monitor frame in physical pixels and shows it without activating.</summary>
    public void Place(double x, double y, double width, double height)
    {
        _x = (int)Math.Round(x);
        _y = (int)Math.Round(y);
        _width = (int)Math.Round(width);
        _height = (int)Math.Round(height);
        if (!IsVisible)
        {
            Show();
        }

        Apply();
    }

    /// <summary>Sets the step number, the marker size for the monitor and the opacity (animated over a tenth of a second).</summary>
    public void Update(int stepNumber, double opacity)
    {
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var diameter = PromptOverlayMath.MarkerDiameter(_width, _height) / Math.Max(0.5, scale);
        _ring.Width = diameter;
        _ring.Height = diameter;
        _ring.StrokeThickness = Math.Max(6, diameter * 0.07);
        _number.FontSize = diameter * 0.5;
        _number.Text = stepNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _label.FontSize = Math.Max(20, diameter * 0.2);
        _marker.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, TimeSpan.FromMilliseconds(100)));
    }

    private void Apply() =>
        OverlayNative.Place(this, _x, _y, _width, _height);
}
