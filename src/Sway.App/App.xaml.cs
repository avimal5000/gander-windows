using System.Windows;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Sway.App;

/// <summary>The tray app. W0 shows only a tray icon with Quit; tracking arrives in later milestones.</summary>
public partial class App : Application
{
    private Forms.NotifyIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Quit Sway", null, (_, _) => Shutdown());

        _tray = new Forms.NotifyIcon
        {
            Icon = Drawing.SystemIcons.Application,
            Text = "Sway",
            ContextMenuStrip = menu,
            Visible = true,
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }

        base.OnExit(e);
    }
}
