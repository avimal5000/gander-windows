using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Gander.App.Presentation;
using Gander.App.Presentation.Design;
using Gander.Core.Controller;
using Shapes = System.Windows.Shapes;

namespace Gander.App.Windows;

/// <summary>
/// The home page of the Gander window: the status, any banner, the Tracking and Focus Lock switches with what they do,
/// the "Looking at" line, the camera, and rows to Calibrate, Settings, the Welcome Tour and Troubleshooting. Thin:
/// <see cref="MainWindowModel.Home"/> decides what is shown; this builds the controls once and copies each new view onto
/// them, so hover and keyboard focus survive the state ticking.
/// </summary>
public sealed class HomePage : UserControl
{
    private readonly Func<TrayMenuInput> _read;
    private readonly Action<TrayCommand, string?> _run;
    private readonly Action<HomeLink> _follow;
    private readonly Func<Action, Action> _subscribe;
    private readonly GanderStyle _style = GanderStyle.Current();
    private readonly Shapes.Ellipse _statusDot = new() { Width = GanderDesign.Size.StatusDot, Height = GanderDesign.Size.StatusDot };
    private readonly TextBlock _status;
    private readonly TextBlock _banner;
    private readonly Button _bannerButton;
    private readonly Border _bannerCard;
    private readonly List<(HomeSwitch Model, CheckBox Box, TextBlock Shortcut)> _switches = [];
    private readonly TextBlock _look;
    private readonly ComboBox _cameras;
    private TrayBanner? _shownBanner;
    private Action? _unsubscribe;
    private bool _updating;

    /// <summary>
    /// Creates the page. <paramref name="read"/> reads the state, <paramref name="run"/> carries out a switch, banner or
    /// camera command, <paramref name="follow"/> opens a row's page, and <paramref name="subscribe"/> calls its argument
    /// when the state changes (it returns the call that stops that).
    /// </summary>
    public HomePage(Func<TrayMenuInput> read, Action<TrayCommand, string?> run, Action<HomeLink> follow, Func<Action, Action> subscribe)
    {
        _read = read;
        _run = run;
        _follow = follow;
        _subscribe = subscribe;
        _style.ApplyTo(this);
        var s = _style;
        var view = MainWindowModel.Home(read());

        // Status, and the one banner when there is one (the menu's banner, with its button).
        _status = s.Label(string.Empty, GanderDesign.Type.Headline);
        _statusDot.Margin = new Thickness(0, 0, GanderDesign.Space.Lg, 0);
        _statusDot.VerticalAlignment = VerticalAlignment.Center;
        var statusRow = new StackPanel { Orientation = Orientation.Horizontal };
        statusRow.Children.Add(_statusDot);
        statusRow.Children.Add(_status);
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);

        _banner = s.Label(string.Empty, GanderDesign.Type.Callout);
        _bannerButton = s.MakeButton(string.Empty, prominent: true);
        _bannerButton.HorizontalAlignment = HorizontalAlignment.Left;
        _bannerButton.Margin = new Thickness(0, GanderDesign.Space.Lg, 0, 0);
        _bannerButton.Click += (_, _) =>
        {
            if (_shownBanner is { } banner)
            {
                _run(banner.Command, null);
            }
        };
        var bannerBody = new StackPanel();
        bannerBody.Children.Add(_banner);
        bannerBody.Children.Add(_bannerButton);
        _bannerCard = s.MakeCard(bannerBody);
        _bannerCard.BorderBrush = s.Amber;
        _bannerCard.BorderThickness = new Thickness(1);
        _bannerCard.Margin = new Thickness(0, GanderDesign.Space.Xl, 0, 0);

        // The two switches, each with what it does underneath.
        var switches = new StackPanel();
        foreach (var model in view.Switches)
        {
            if (switches.Children.Count > 0)
            {
                switches.Children.Add(new Border { Height = 1, Background = s.Line, Margin = new Thickness(0, GanderDesign.Space.Xl, 0, GanderDesign.Space.Xl) });
            }

            var box = s.MakeSwitch(model.Title);
            box.FontWeight = FontWeights.SemiBold;
            box.ToolTip = model.Help;
            AutomationProperties.SetName(box, model.Title);
            AutomationProperties.SetHelpText(box, model.Help);
            var command = model.Command;
            box.Click += (_, _) =>
            {
                if (!_updating)
                {
                    _run(command, null);
                }
            };
            var help = s.Label(model.Help, GanderDesign.Type.Caption, secondary: true);
            help.Margin = new Thickness(0, GanderDesign.Space.Sm, 0, 0);
            var shortcut = s.Label(string.Empty, GanderDesign.Type.Caption, secondary: true);
            shortcut.Margin = new Thickness(0, GanderDesign.Space.Sm, 0, 0);
            switches.Children.Add(box);
            switches.Children.Add(help);
            switches.Children.Add(shortcut);
            _switches.Add((model, box, shortcut));
        }

        _look = s.Label(string.Empty, GanderDesign.Type.Callout, secondary: true);
        _look.Margin = new Thickness(0, GanderDesign.Space.Xl, 0, 0);
        switches.Children.Add(_look);
        var switchCard = s.MakeCard(switches);
        switchCard.Margin = new Thickness(0, GanderDesign.Space.Section, 0, 0);

        // The camera, as in the menu and Settings.
        _cameras = new ComboBox { MinWidth = 220, Background = s.Card, Foreground = s.Text, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(_cameras, "Camera");
        _cameras.ToolTip = TrayMenuModel.Help.Camera;
        _cameras.SelectionChanged += (_, _) =>
        {
            if (!_updating && _cameras.SelectedItem is ComboBoxItem { Tag: string id })
            {
                _run(TrayCommand.SelectCamera, id.Length == 0 ? null : id);
            }
        };
        var cameraLabel = s.Label("Camera", GanderDesign.Type.Headline, wrap: false);
        cameraLabel.VerticalAlignment = VerticalAlignment.Center;
        var cameraRow = new Grid();
        cameraRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cameraRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_cameras, 1);
        cameraRow.Children.Add(cameraLabel);
        cameraRow.Children.Add(_cameras);
        var cameraCard = s.MakeCard(cameraRow);
        cameraCard.Margin = new Thickness(0, GanderDesign.Space.Section, 0, 0);

        // Where to go from here.
        var links = new StackPanel { Margin = new Thickness(0, GanderDesign.Space.Section, 0, 0) };
        foreach (var link in view.Links)
        {
            var row = s.MakeRowButton(link.Title, link.Help);
            row.Margin = new Thickness(0, 0, 0, GanderDesign.Space.Lg);
            row.Click += (_, _) => _follow(link);
            links.Children.Add(row);
        }

        var quit = s.MakeButton("Quit Gander");
        quit.HorizontalAlignment = HorizontalAlignment.Right;
        quit.ToolTip = "Turns the camera off and closes Gander. It starts again from the Start menu.";
        quit.Click += (_, _) => _run(TrayCommand.Quit, null);
        quit.Margin = new Thickness(0, GanderDesign.Space.Sm, 0, 0);

        var stack = new StackPanel { Margin = new Thickness(GanderDesign.Space.SetupPadding, GanderDesign.Space.Lg, GanderDesign.Space.SetupPadding, GanderDesign.Space.SetupPadding) };
        stack.Children.Add(statusRow);
        stack.Children.Add(_bannerCard);
        stack.Children.Add(switchCard);
        stack.Children.Add(cameraCard);
        stack.Children.Add(links);
        stack.Children.Add(quit);
        Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };

        Apply();
        Loaded += (_, _) =>
        {
            _unsubscribe ??= _subscribe(Apply);
            Apply();
        };
        Unloaded += (_, _) =>
        {
            _unsubscribe?.Invoke();
            _unsubscribe = null;
        };
    }

    private void Apply()
    {
        var input = _read();
        var view = MainWindowModel.Home(input);
        _updating = true;
        try
        {
            _status.Text = view.StatusLine;
            _statusDot.Fill = GanderStyle.Brush(view.Tone switch
            {
                StatusTone.Active => GanderDesign.Forest,
                StatusTone.Attention => GanderDesign.Amber,
                StatusTone.Problem => GanderDesign.Brick,
                _ => _style.Theme.Flat(_style.Theme.TextSecondary),
            });

            _shownBanner = view.Banner;
            _bannerCard.Visibility = view.Banner is null ? Visibility.Collapsed : Visibility.Visible;
            _banner.Text = view.Banner?.Text ?? string.Empty;
            _bannerButton.Visibility = view.Banner?.ButtonText is null ? Visibility.Collapsed : Visibility.Visible;
            _bannerButton.Content = view.Banner?.ButtonText ?? string.Empty;

            foreach (var (model, box, shortcut) in _switches)
            {
                var now = view.Switches.First(s => s.Command == model.Command);
                box.IsChecked = now.IsOn;
                shortcut.Text = now.ShortcutText is { } keys ? $"Shortcut: {keys}" : string.Empty;
                shortcut.Visibility = now.ShortcutText is null ? Visibility.Collapsed : Visibility.Visible;
            }

            _look.Text = view.LookText;
            FillCameras(input);
        }
        finally
        {
            _updating = false;
        }
    }

    private void FillCameras(TrayMenuInput input)
    {
        var items = new List<(string Name, string Id)> { (SettingsWording.DefaultCamera, string.Empty) };
        items.AddRange(input.Cameras.Select(c => (c.Name, c.Id)));
        if (input.SelectedCameraId is { } missing && input.Cameras.All(c => c.Id != missing))
        {
            items.Add((SettingsWording.UnavailableCamera, missing));
        }

        // Rebuild the list only when it changed, so an open drop-down is not reset by every state tick.
        var current = _cameras.Items.Cast<ComboBoxItem>().Select(i => ((string)i.Content, (string)i.Tag)).ToList();
        if (!current.SequenceEqual(items))
        {
            _cameras.Items.Clear();
            foreach (var (name, id) in items)
            {
                _cameras.Items.Add(new ComboBoxItem { Content = name, Tag = id });
            }
        }

        var selected = input.SelectedCameraId ?? string.Empty;
        _cameras.SelectedIndex = Math.Max(0, items.FindIndex(i => i.Id == selected));
    }
}
