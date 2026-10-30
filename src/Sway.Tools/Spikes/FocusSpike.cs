namespace Sway.Tools.Spikes;

/// <summary>
/// Spike S2: can a background program give another window the keyboard focus, without moving the cursor?
/// Each attempt asks the user to click a window on one monitor first, so the foreground belongs to another app, the
/// way it will when Sway runs from the tray. Then it tries to focus the topmost window on the next monitor.
/// </summary>
internal static class FocusSpike
{
    private const int Rounds = 2;
    private const int CountdownSeconds = 5;

    private enum Method
    {
        /// <summary>SetForegroundWindow alone.</summary>
        Plain,

        /// <summary>Attach to the foreground thread's input queue first, then SetForegroundWindow.</summary>
        AttachInput,
    }

    private sealed record Attempt(Method Method, string From, string To, string Target, bool? Elevated, bool Returned, bool Focused, bool KeyboardFocus, bool CursorMoved, string Note);

    internal static void Run()
    {
        using var report = new Report("sway-focus.txt");
        report.Line($"SwayTools focus test, {DateTime.Now:yyyy-MM-dd HH:mm}");
        var monitors = Survey.Monitors();
        if (monitors.Count < 2)
        {
            report.Line("This test needs at least two monitors. Plug them in and run it again.");
            return;
        }

        report.Line($"{monitors.Count} monitors. For each attempt you click a window on one monitor, wait, and Sway's test");
        report.Line("tries to move the focus to the topmost window on the next monitor. Do not touch the mouse or keyboard");
        report.Line($"after the {CountdownSeconds}-second countdown ends.");
        report.Line();

        var attempts = new List<Attempt>();
        foreach (var method in Enum.GetValues<Method>())
        {
            for (var round = 1; round <= Rounds; round++)
            {
                for (var index = 0; index < monitors.Count; index++)
                {
                    attempts.Add(Try(report, monitors, index, (index + 1) % monitors.Count, method, round));
                }
            }
        }

        Summarize(report, attempts);
        report.Line();
        report.Line($"Also saved to: {report.Path}");
    }

    private static Attempt Try(Report report, List<MonitorInfo> monitors, int from, int to, Method method, int round)
    {
        var fromName = $"monitor {from + 1}";
        var toName = $"monitor {to + 1}";
        report.Line($"--- {method}, round {round}: {fromName} to {toName} ---");
        report.Line($"Click any window on {fromName} (not this console) now.");
        for (var second = CountdownSeconds; second > 0; second--)
        {
            Console.Write($"  {second}... ");
            Thread.Sleep(1000);
        }

        Console.WriteLine();

        var origin = Native.GetForegroundWindow();
        var (windows, _) = Survey.Windows();
        var target = windows.FirstOrDefault(window => window.Monitor == monitors[to].Handle && window.Handle != origin);
        if (target is null)
        {
            report.Line($"No window to focus on {toName}. Skipped.");
            return new Attempt(method, fromName, toName, "none", null, false, false, false, false, "no window on the target monitor");
        }

        var originMonitor = Native.MonitorFromWindow(origin, Native.MonitorDefaultToNearest);
        var note = originMonitor == monitors[from].Handle ? string.Empty : "the click was not on the expected monitor";
        var describedTarget = $"{target.Process} [{target.ClassName}]";

        Native.GetCursorPos(out var before);
        var returned = Focus(target, origin, method);
        var focused = false;
        for (var waited = 0; waited < 600 && !focused; waited += 50)
        {
            Thread.Sleep(50);
            focused = Native.GetForegroundWindow() == target.Handle;
        }

        var info = new Native.GuiThreadInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<Native.GuiThreadInfo>() };
        var keyboardFocus = Native.GetGUIThreadInfo(target.ThreadId, ref info) && info.Focus != IntPtr.Zero;
        Native.GetCursorPos(out var after);
        var cursorMoved = before.X != after.X || before.Y != after.Y;

        report.Line($"Target: {describedTarget}{(target.Elevated == true ? " (ELEVATED)" : string.Empty)}");
        report.Line($"SetForegroundWindow returned {returned}; target is now foreground: {focused}; it holds keyboard focus: {keyboardFocus}; cursor moved: {cursorMoved}. {note}".TrimEnd());
        return new Attempt(method, fromName, toName, describedTarget, target.Elevated, returned, focused, keyboardFocus, cursorMoved, note);
    }

    private static bool Focus(WindowInfo target, IntPtr origin, Method method)
    {
        if (Native.IsIconic(target.Handle))
        {
            Native.ShowWindow(target.Handle, Native.SwRestore);
        }

        if (method == Method.Plain)
        {
            return Native.SetForegroundWindow(target.Handle);
        }

        var current = Native.GetCurrentThreadId();
        var foregroundThread = Native.GetWindowThreadProcessId(origin, out _);
        var attached = foregroundThread != 0 && foregroundThread != current && Native.AttachThreadInput(current, foregroundThread, true);
        try
        {
            Native.BringWindowToTop(target.Handle);
            return Native.SetForegroundWindow(target.Handle);
        }
        finally
        {
            if (attached)
            {
                Native.AttachThreadInput(current, foregroundThread, false);
            }
        }
    }

    private static void Summarize(Report report, List<Attempt> attempts)
    {
        report.Line();
        report.Line("== Summary ==");
        foreach (var group in attempts.GroupBy(attempt => attempt.Method))
        {
            var tried = group.Count(attempt => attempt.Target != "none");
            var worked = group.Count(attempt => attempt.Focused);
            var keyboard = group.Count(attempt => attempt.Focused && attempt.KeyboardFocus);
            var moved = group.Count(attempt => attempt.CursorMoved);
            report.Line($"{group.Key}: focused {worked} of {tried}; keyboard focus confirmed {keyboard}; cursor moved {moved} time(s)");
        }

        foreach (var attempt in attempts.Where(attempt => attempt.Target != "none" && !attempt.Focused))
        {
            report.Line($"  failed: {attempt.Method}, {attempt.From} to {attempt.To}, target {attempt.Target}{(attempt.Elevated == true ? " (elevated)" : string.Empty)}");
        }
    }
}
