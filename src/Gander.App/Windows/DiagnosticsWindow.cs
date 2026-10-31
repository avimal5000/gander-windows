using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Gander.App.Hosting;
using Gander.App.Presentation.Design;
using Gander.Core.Controller;

namespace Gander.App.Windows;

/// <summary>
/// Read-only diagnostics: the text block from <see cref="Presentation.DiagnosticsText"/>, refreshed when the controller's
/// state changes, and a button that copies it. Styled from the shared design tokens (see <see cref="GanderStyle"/>), light or dark by the apps theme.
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

        var style = GanderStyle.Current();
        style.ApplyTo(this);
        Title = "Gander Diagnostics";
        Width = 640;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _text = new TextBox
        {
            IsReadOnly = true,
            FontFamily = DesignFonts.Wpf(GanderDesign.Mono),
            FontSize = GanderDesign.Type.Caption.Size,
            Background = style.Card,
            Foreground = style.Text,
            BorderBrush = style.Line,
            BorderThickness = new Thickness(GanderDesign.Space.Hairline),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(GanderDesign.Space.Lg),
            Margin = new Thickness(GanderDesign.Space.Xl + 4, GanderDesign.Space.Xl + 4, GanderDesign.Space.Xl + 4, 0),
        };

        var copy = style.MakeButton("Copy diagnostics", prominent: true);
        copy.Margin = new Thickness(0, 0, GanderDesign.Space.Lg, 0);
        copy.Click += (_, _) => Copy();
        var close = style.MakeButton("Close");
        close.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(GanderDesign.Space.Xl + 4) };
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
