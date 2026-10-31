using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Sway.App.Presentation;

namespace Sway.App.Windows;

/// <summary>
/// A borderless, topmost window over one monitor showing a large "Look here" marker. It never takes focus and clicks go
/// through it (WS_EX_TRANSPARENT, NOACTIVATE, TOOLWINDOW); it never touches the pointer.
/// </summary>
public sealed class PromptOverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExToolWindow = 0x80;
    private const int WsExNoActivate = 0x08000000;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndTopmost = new(-1);

    private readonly Ellipse _ring = new() { Stroke = new SolidColorBrush(Color.FromRgb(0x2F, 0x5D, 0x3A)), Fill = new SolidColorBrush(Color.FromArgb(0xCC, 0xF6, 0xF3, 0xEA)) };
    private readonly TextBlock _number = new() { FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x2F, 0x5D, 0x3A)), HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _label = new() { Text = "Look here", Foreground = new SolidColorBrush(Color.FromRgb(0x2F, 0x5D, 0x3A)), FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Grid _marker = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0 };
    private int _x;
    private int _y;
    private int _width;
    private int _height;

    /// <summary>Creates the (hidden) overlay.</summary>
    public PromptOverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        ResizeMode = ResizeMode.NoResize;
        Title = "Sway prompt";
        Left = -32000;
        Top = -32000;
        Width = 100;
        Height = 100;

        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var ringHolder = new Grid();
        ringHolder.Children.Add(_ring);
        ringHolder.Children.Add(_number);
        _number.VerticalAlignment = VerticalAlignment.Center;
        stack.Children.Add(ringHolder);
        stack.Children.Add(_label);
        _marker.Children.Add(stack);
        Content = new Grid { Children = { _marker } };
        SourceInitialized += (_, _) => MakeClickThrough();
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

    private void Apply()
    {
        if (new WindowInteropHelper(this).Handle is { } handle && handle != IntPtr.Zero)
        {
            SetWindowPos(handle, HwndTopmost, _x, _y, _width, _height, SwpNoActivate | SwpShowWindow);
        }
    }

    private void MakeClickThrough()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, style | WsExTransparent | WsExNoActivate | WsExToolWindow);
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
