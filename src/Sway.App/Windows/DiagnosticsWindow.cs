using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Sway.App.Hosting;
using Sway.Core.Controller;

namespace Sway.App.Windows;

/// <summary>
/// Read-only diagnostics: the text block from <see cref="Presentation.DiagnosticsText"/>, refreshed when the controller's
/// state changes, and a button that copies it. Plain WPF with system fonts, light or dark by the apps theme.
/// </summary>
public sealed class DiagnosticsWindow : Window
{
    private readonly TrackingController _controller;
    private readonly Func<string> _build;
    private readonly TextBox _text;
    private readonly DispatcherTimer _timer;

    /// <summary>Creates the window; <paramref name="build"/> returns the current diagnostics text.</summary>
    public DiagnosticsWindow(TrackingController controller, Func<string> build)
    {
        _controller = controller;
        _build = build;

        var light = SystemInfo.IsLightApps();
        var background = light ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20));
        var foreground = light ? Brushes.Black : new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));
        Title = "Sway Diagnostics";
        Width = 640;
        Height = 560;
        FontFamily = SystemFonts.MessageFontFamily;
        Background = background;
        Foreground = foreground;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _text = new TextBox
        {
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Background = background,
            Foreground = foreground,
            BorderThickness = new Thickness(0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(12, 12, 12, 0),
        };

        var copy = new Button { Content = "Copy diagnostics", Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0) };
        copy.Click += (_, _) => Copy();
        var close = new Button { Content = "Close", Padding = new Thickness(14, 5, 14, 5) };
        close.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12) };
        buttons.Children.Add(copy);
        buttons.Children.Add(close);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(buttons, 1);
        grid.Children.Add(_text);
        grid.Children.Add(buttons);
        Content = grid;

        Refresh();
        _controller.StateChanged += Refresh;

        // The live pose and the counters change at the frame rate, which StateChanged does not report: refresh 4 times a second.
        _timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Closed += (_, _) =>
        {
            _timer.Stop();
            _controller.StateChanged -= Refresh;
        };
    }

    private void Refresh()
    {
        var next = _build();
        if (next == _text.Text)
        {
            return;
        }

        var offset = _text.VerticalOffset;
        _text.Text = next;
        _text.ScrollToVerticalOffset(offset);
    }

    private void Copy()
    {
        try
        {
            Clipboard.SetDataObject(_build(), true);
        }
        catch (Exception)
        {
            // The clipboard can be locked by another app; the text stays visible to select by hand.
        }
    }
}
