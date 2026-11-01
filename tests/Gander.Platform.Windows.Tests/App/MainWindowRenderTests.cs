using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Gander.App.Hosting;
using Gander.App.Presentation;
using Gander.App.Tray;
using Gander.App.Windows;
using Gander.Core.Calibration;
using Gander.Core.Platform;
using Gander.Core.Tests.Controller;

namespace Gander.Platform.Windows.Tests.App;

/// <summary>
/// Builds the real Gander window on a fake controller, opens every page in light and dark, and checks that nothing
/// sticks out past the window's right edge (the intro's Next button once did). When GANDER_SCREENSHOTS names a folder,
/// each page is also saved there as a PNG, which CI uploads so the layout can be looked at without a PC.
/// </summary>
public sealed class MainWindowRenderTests
{
    private static readonly string? ShotFolder = Environment.GetEnvironmentVariable("GANDER_SCREENSHOTS");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EveryPageFitsTheWindow(bool light) => OnSta(() =>
    {
        using var calibrated = Harness.Make(calibrated: true, trackingSaved: true);
        var window = MakeWindow(calibrated, light);
        try
        {
            foreach (var page in Enum.GetValues<GanderPage>())
            {
                window.Open(page);
                Settle();
                Check(window, $"{page}-{Theme(light)}");
                window.Open(GanderPage.Home);
            }
        }
        finally
        {
            window.Close();
        }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EveryIntroPageFitsTheWindow(bool light) => OnSta(() =>
    {
        // Nothing calibrated: Set up opens on the walkthrough.
        using var fresh = Harness.Make(layout: Fixtures.TwoDisplays);
        var session = new SetupSession(fresh.Controller, fresh.Main, fresh.Scheduler, () => fresh.Clock.Now);
        var window = MakeWindow(fresh, light, session);
        try
        {
            window.Open(GanderPage.Setup);
            for (var page = 0; page < SetupPresentation.IntroPages.Count; page++)
            {
                Settle();
                Check(window, $"Intro{page + 1}-{Theme(light)}");
                session.IntroNext();
                fresh.Drain();
            }
        }
        finally
        {
            window.Close();
        }
    });

    private static string Theme(bool light) => light ? "light" : "dark";

    private static MainWindow MakeWindow(Harness h, bool light, SetupSession? session = null)
    {
        AppTheme.LightOverride = light;
        AppTheme.AnimationsOff = true;
        var preferences = new AppPreferences(h.Settings);
        session ??= new SetupSession(h.Controller, h.Main, h.Scheduler, () => h.Clock.Now);
        FrameworkElement Make(GanderPage page) => page switch
        {
            GanderPage.Settings => new SettingsPage(h.Controller, new FakeLoginItem(), () => { }, preferences),
            GanderPage.Setup => new SetupPage(session),
            GanderPage.Troubleshooting => new TroubleshootingPage(_ => { }),
            _ => new HomePage(() => MenuState.Read(h.Controller, true), (_, _) => { }, _ => { }, _ => () => { }),
        };

        var window = new MainWindow(Make)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = -20000,
            ShowActivated = false,
            ShowInTaskbar = false,
        };
        window.Show();
        return window;
    }

    // Lets layout, Loaded and posted work run.
    private static void Settle()
    {
        for (var i = 0; i < 3; i++)
        {
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
        }
    }

    private static void Check(MainWindow window, string name)
    {
        var root = (FrameworkElement)window.Content;
        root.UpdateLayout();
        var width = root.ActualWidth;
        var outside = new List<string>();
        foreach (var element in Descendants(root).OfType<FrameworkElement>())
        {
            if (!element.IsVisible || element.ActualWidth <= 0 || element is not (Button or TextBlock or CheckBox or ComboBox))
            {
                continue;
            }

            var box = element.TransformToAncestor(root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            if (box.Right > width + 0.5 || box.Left < -0.5)
            {
                outside.Add($"{element.GetType().Name} '{Describe(element)}' spans {box.Left:0}–{box.Right:0} of {width:0}");
            }
        }

        Save(root, name);
        Assert.True(outside.Count == 0, $"{name}: {string.Join("; ", outside)}");
    }

    private static string Describe(FrameworkElement element) => element switch
    {
        TextBlock text => text.Text,
        ContentControl { Content: string text } => text,
        _ => element.Name,
    };

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var deeper in Descendants(child))
            {
                yield return deeper;
            }
        }
    }

    private static void Save(FrameworkElement root, string name)
    {
        if (string.IsNullOrEmpty(ShotFolder))
        {
            return;
        }

        // 150% scaling, a common laptop setting.
        const double dpi = 144;
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(root.ActualWidth * dpi / 96),
            (int)Math.Ceiling(root.ActualHeight * dpi / 96),
            dpi,
            dpi,
            PixelFormats.Pbgra32);
        var frame = new DrawingVisual();
        using (var context = frame.RenderOpen())
        {
            var background = (root.Parent as Window)?.Background ?? Brushes.White;
            context.DrawRectangle(background, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            context.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        }

        bitmap.Render(frame);
        Directory.CreateDirectory(ShotFolder);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(ShotFolder, $"{name}.png"));
        encoder.Save(file);
    }

    // WPF needs a single-threaded apartment with its own dispatcher.
    private static void OnSta(Action action)
    {
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                error = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                AppTheme.LightOverride = null;
                AppTheme.AnimationsOff = false;
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }

    private sealed class FakeLoginItem : ILoginItem
    {
        public LoginItemStatus Status { get; private set; } = LoginItemStatus.Disabled;

        public void SetEnabled(bool enabled) => Status = enabled ? LoginItemStatus.Enabled : LoginItemStatus.Disabled;
    }
}
