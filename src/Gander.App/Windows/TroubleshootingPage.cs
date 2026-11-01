using System;
using System.Windows;
using System.Windows.Controls;
using Gander.App.Presentation;
using Gander.App.Presentation.Design;

namespace Gander.App.Windows;

/// <summary>
/// The Troubleshooting page of the Gander window: a row per tool (Pose Debug, Head-Pose Gate Test, Focus Test,
/// Diagnostics). Each opens a window of its own, because the tools show live data beside whatever you are testing.
/// </summary>
public sealed class TroubleshootingPage : UserControl
{
    /// <summary>The line at the top of the page.</summary>
    public const string Intro =
        "Gander works without these. They help when switching goes wrong: they show what the camera sees, how well Gander tells your displays apart, and whether it can move focus.";

    /// <summary>Creates the page; <paramref name="run"/> opens a tool's window.</summary>
    public TroubleshootingPage(Action<TrayCommand> run)
    {
        var s = GanderStyle.Current();
        s.ApplyTo(this);
        var stack = new StackPanel { Margin = new Thickness(GanderDesign.Space.SetupPadding, GanderDesign.Space.Lg, GanderDesign.Space.SetupPadding, GanderDesign.Space.SetupPadding) };
        var intro = s.Label(Intro, GanderDesign.Type.Callout, secondary: true);
        intro.Margin = new Thickness(0, 0, 0, GanderDesign.Space.Section);
        stack.Children.Add(intro);
        foreach (var tool in MainWindowModel.Troubleshooting)
        {
            var row = s.MakeRowButton(tool.Title, tool.Help);
            row.Margin = new Thickness(0, 0, 0, GanderDesign.Space.Lg);
            var command = tool.Command;
            row.Click += (_, _) => run(command);
            stack.Children.Add(row);
        }

        Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
    }
}
