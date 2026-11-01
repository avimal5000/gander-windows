using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Gander.App.Hosting;
using Gander.App.Presentation;
using Gander.App.Presentation.Design;

namespace Gander.App.Windows;

/// <summary>
/// The Gander window that opens when you click the goose in the tray: one window whose pages are Home, Settings, Set
/// up and Troubleshooting. A header shows the goose and "Gander" on Home, and Back plus the page's name elsewhere; Back
/// (or Esc) returns to the page before. Pages are made fresh each time they are shown, so each starts from the current
/// state and lets go of it (the setup session, the controller's events) when you leave.
/// </summary>
public sealed class MainWindow : Window
{
    /// <summary>The window's width: room for the Setup and Settings pages, which were 520 wide as windows.</summary>
    public const double PageWidth = 560;

    private readonly Func<GanderPage, FrameworkElement> _makePage;
    private readonly PageHistory _history = new();
    private readonly GanderStyle _style = GanderStyle.Current();
    private readonly Border _header = new();
    private readonly Button _back;
    private readonly TextBlock _title;
    private readonly FrameworkElement _mark;
    private readonly ContentControl _host = new() { Focusable = false };

    /// <summary>Creates the window on Home. <paramref name="makePage"/> builds a page when it is shown.</summary>
    public MainWindow(Func<GanderPage, FrameworkElement> makePage)
    {
        _makePage = makePage;
        Title = MainWindowModel.WindowTitle;
        Width = PageWidth;
        Height = Math.Min(720, Math.Max(480, SystemParameters.WorkArea.Height * 0.9));
        MinHeight = 420;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _style.ApplyTo(this);

        _back = _style.MakeButton($"‹  {MainWindowModel.BackText}");
        _back.Background = System.Windows.Media.Brushes.Transparent;
        _back.BorderBrush = System.Windows.Media.Brushes.Transparent;
        _back.Margin = new Thickness(-8, 0, GanderDesign.Space.Lg, 0);
        _back.VerticalAlignment = VerticalAlignment.Center;
        _back.ToolTip = "Back to the page before (Esc)";
        AutomationProperties.SetName(_back, MainWindowModel.BackText);
        _back.Click += (_, _) => Back();

        _mark = GanderStyle.MakeMark(GanderDesign.Size.SetupMark, _style.Theme.Green, _style.Theme.Green);
        _mark.Margin = new Thickness(0, 0, 10, 0);
        _mark.VerticalAlignment = VerticalAlignment.Center;
        _title = _style.Label(string.Empty, GanderDesign.Type.Title2, wrap: false);
        _title.VerticalAlignment = VerticalAlignment.Center;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_back);
        row.Children.Add(_mark);
        row.Children.Add(_title);
        _header.Child = row;
        _header.Padding = new Thickness(GanderDesign.Space.SetupPadding, GanderDesign.Space.Xl + 4, GanderDesign.Space.SetupPadding, GanderDesign.Space.Lg);

        var layout = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_header, Dock.Top);
        layout.Children.Add(_header);
        layout.Children.Add(_host);
        Content = layout;

        KeyDown += OnKeyDown;
        ShowPage();
    }

    /// <summary>The page shown now.</summary>
    public GanderPage CurrentPage => _history.Current;

    /// <summary>The element of the page shown now (for tests and screenshots).</summary>
    public FrameworkElement? CurrentElement => _host.Content as FrameworkElement;

    /// <summary>Shows <paramref name="page"/>; a page already on the way back is returned to rather than opened twice.</summary>
    public void Open(GanderPage page)
    {
        if (page == _history.Current && _host.Content is not null)
        {
            return;
        }

        _history.Open(page);
        ShowPage();
    }

    /// <summary>Goes back a page; nothing happens on Home.</summary>
    public void Back()
    {
        if (_history.Back())
        {
            ShowPage();
        }
    }

    /// <summary>Leaves <paramref name="page"/> if it is the one shown (Setup calls this when it finishes).</summary>
    public void Leave(GanderPage page)
    {
        if (_history.Current == page)
        {
            Back();
        }
    }

    // Esc goes back, unless the page used it first (cancelling a calibration, or recording a shortcut).
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && MainWindowModel.HasBack(_history.Current))
        {
            Back();
            e.Handled = true;
        }
    }

    private void ShowPage()
    {
        var page = _history.Current;
        var element = _makePage(page);
        _host.Content = element;

        var home = !MainWindowModel.HasBack(page);
        _back.Visibility = home ? Visibility.Collapsed : Visibility.Visible;
        _mark.Visibility = home ? Visibility.Visible : Visibility.Collapsed;
        _title.Text = MainWindowModel.PageTitle(page);

        // The header and the window take the page's background (the tour sits on ivory paper).
        var background = new Binding(nameof(Background)) { Source = element };
        _header.SetBinding(Border.BackgroundProperty, background);
        SetBinding(BackgroundProperty, background);

        if (AppTheme.Animate)
        {
            element.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromSeconds(GanderDesign.Motion.Fade)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
        }

        // Other pages put focus on their own main button (Setup's Next, for one); Home starts at its first control.
        if (home)
        {
            Dispatcher.BeginInvoke(() => element.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)));
        }
    }
}
