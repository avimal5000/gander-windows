using System.Windows;
using System.Windows.Controls;

namespace Sway.App.Windows;

/// <summary>Stands in for the calibration window until the orchestrator wires the real one.</summary>
public sealed class CalibrationPlaceholderWindow : Window
{
    /// <summary>Creates the window.</summary>
    public CalibrationPlaceholderWindow()
    {
        Title = "Calibrate Sway";
        Width = 360;
        Height = 160;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = new TextBlock
        {
            Text = "Calibration coming next",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 16,
        };
    }
}
