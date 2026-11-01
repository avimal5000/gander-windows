using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Gander.App.Presentation;
using Gander.App.Presentation.Design;
using Gander.Core.Calibration;
using Shapes = System.Windows.Shapes;

namespace Gander.App.Windows;

/// <summary>
/// The paged intro on the welcome step: four pages stacked in one grid (so the window keeps one size from page to page
/// and no text is clipped), the page dots centered on their own row, and below them Skip on the left with Back and Next
/// on the right. (Dots between the buttons on one row left too little room: Next ran off the window's right edge.) Thin: <see cref="SetupViewBuilder"/> says
/// which page and which buttons; this copies that onto controls and reports clicks.
/// </summary>
internal sealed class OnboardingIntro
{
    private readonly GanderStyle _style;
    private readonly Grid _pages = new();
    private readonly FrameworkElement[] _pageViews;
    private readonly Shapes.Ellipse[] _dots;
    private readonly StackPanel _dotRow = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _skip;
    private readonly Button _back;
    private readonly Button _next;
    private SetupView? _view;
    private int _shownPage = -1;

    public OnboardingIntro(GanderStyle style)
    {
        _style = style;
        var art = new OnboardingArt(style);
        _pageViews = new FrameworkElement[SetupPresentation.IntroPages.Count];
        _dots = new Shapes.Ellipse[_pageViews.Length];
        for (var i = 0; i < _pageViews.Length; i++)
        {
            _pageViews[i] = art.BuildPage(SetupPresentation.IntroPages[i]);
            _pageViews[i].Visibility = Visibility.Hidden;
            _pages.Children.Add(_pageViews[i]);

            _dots[i] = new Shapes.Ellipse { Width = 8, Height = 8, Margin = new Thickness(4, 0, 4, 0), StrokeThickness = 1.5 };
            _dotRow.Children.Add(_dots[i]);
        }

        AutomationProperties.SetName(_dotRow, SetupPresentation.IntroPageLine(0));
        _skip = style.MakeButton(SetupPresentation.IntroSkipButton);
        _skip.Background = Brushes.Transparent;
        _skip.BorderBrush = Brushes.Transparent;
        _skip.Foreground = style.Secondary;
        // Skip is plain text, so the button template's hover fade does nothing visible: darken the text instead.
        _skip.MouseEnter += (_, _) => _skip.Foreground = style.Text;
        _skip.MouseLeave += (_, _) => _skip.Foreground = style.Secondary;
        _skip.Cursor = System.Windows.Input.Cursors.Hand;
        _back = style.MakeButton(SetupPresentation.IntroBackButton);
        _back.MinWidth = 84;
        _next = style.MakeButton(SetupPresentation.IntroNextButton, prominent: true);
        _next.MinWidth = 120;
        _next.Padding = new Thickness(22, 8, 22, 8);
        _next.Margin = new Thickness(GanderDesign.Space.Xl, 0, 0, 0);
        _next.IsDefault = true;

        _skip.Click += (_, _) => Invoked?.Invoke(_view?.Intro?.Skip);
        _back.Click += (_, _) => Invoked?.Invoke(_view?.Secondary);
        _next.Click += (_, _) => Invoked?.Invoke(_view?.Primary);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(_back);
        buttons.Children.Add(_next);
        var nav = new Grid { Margin = new Thickness(0, GanderDesign.Space.Xl, 0, 0) };
        nav.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        nav.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _skip.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetColumn(_skip, 0);
        Grid.SetColumn(buttons, 1);
        nav.Children.Add(_skip);
        nav.Children.Add(buttons);

        _dotRow.Margin = new Thickness(0, 18, 0, 0);
        var root = new StackPanel();
        root.Children.Add(_pages);
        root.Children.Add(_dotRow);
        root.Children.Add(nav);
        Root = root;
    }

    /// <summary>A button was pressed (null when the view has none there).</summary>
    public event Action<SetupButton?>? Invoked;

    /// <summary>The whole intro, to put in the window.</summary>
    public FrameworkElement Root { get; }

    /// <summary>The primary button, for keyboard focus.</summary>
    public Button Next => _next;

    /// <summary>Shows the page and buttons of <paramref name="view"/> (a welcome view with an <see cref="SetupView.Intro"/>).</summary>
    public void Show(SetupView view)
    {
        _view = view;
        if (view.Intro is not { } intro)
        {
            return;
        }

        var changed = _shownPage != intro.Page;
        for (var i = 0; i < _pageViews.Length; i++)
        {
            _pageViews[i].Visibility = i == intro.Page ? Visibility.Visible : Visibility.Hidden;
            _dots[i].Fill = i == intro.Page ? _style.Accent : Brushes.Transparent;
            _dots[i].Stroke = i == intro.Page ? _style.Accent : _style.Secondary;
        }

        AutomationProperties.SetName(_dotRow, intro.PageLine);
        SetButton(_next, view.Primary);
        SetButton(_back, view.Secondary);
        SetButton(_skip, intro.Skip);
        if (changed && _shownPage >= 0 && SystemParameters.ClientAreaAnimation)
        {
            _pages.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromSeconds(GanderDesign.Motion.Fade)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
        }

        _shownPage = intro.Page;
    }

    // Hidden, not collapsed, so the buttons around it do not move.
    private static void SetButton(Button button, SetupButton? model)
    {
        button.Visibility = model is null ? Visibility.Hidden : Visibility.Visible;
        button.IsEnabled = model?.Enabled ?? false;
        if (model is null)
        {
            return;
        }

        button.Content = model.Label;
        AutomationProperties.SetName(button, model.Label);
    }
}
