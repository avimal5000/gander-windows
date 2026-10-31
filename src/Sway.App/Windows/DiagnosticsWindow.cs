using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Sway.App.Hosting;
using Sway.App.Presentation.Design;
using Sway.Core.Controller;

namespace Sway.App.Windows;

/// <summary>
/// Read-only diagnostics: the text block from <see cref="Presentation.DiagnosticsText"/>, refreshed when the controller's
/// state changes, and a button that copies it. Styled from the shared design tokens (see <see cref="SwayStyle"/>), light or dark by the apps theme.
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

        var style = SwayStyle.Current();
        style.ApplyTo(this);
        Title = "Sway Diagnostics";
        Width = 640;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _text = new TextBox
        {
            IsReadOnly = true,
            FontFamily = DesignFonts.Wpf(SwayDesign.Mono),
            FontSize = SwayDesign.Type.Caption.Size,
            Background = style.Card,
            Foreground = style.Text,
            BorderBrush = style.Line,
            BorderThickness = new Thickness(SwayDesign.Space.Hairline),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(SwayDesign.Space.Lg),
            Margin = new Thickness(SwayDesign.Space.Xl + 4, SwayDesign.Space.Xl + 4, SwayDesign.Space.Xl + 4, 0),
        };

        var copy = style.MakeButton("Copy diagnostics", prominent: true);
        copy.Margin = new Thickness(0, 0, SwayDesign.Space.Lg, 0);
        copy.Click += (_, _) => Copy();
        var close = style.MakeButton("Close");
        close.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(SwayDesign.Space.Xl + 4) };
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
