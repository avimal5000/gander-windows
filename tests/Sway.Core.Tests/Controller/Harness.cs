using System.Text.Json;
using Sway.Core.Controller;
using Sway.Core.Platform;
using Sway.Core.Runtime;
using Sway.Core.Tests.Runtime;

namespace Sway.Core.Tests.Controller;

/// <summary>A <see cref="TrackingController"/> wired to fakes for every collaborator, with a manual clock and scheduler. Nothing sleeps.</summary>
public sealed class Harness : IDisposable
{
    private Harness(TrackingEnvironment environment, TrackingController controller, TestClock clock)
    {
        Environment = environment;
        Controller = controller;
        Clock = clock;
    }

    public TrackingEnvironment Environment { get; }

    public TrackingController Controller { get; }

    public FakePoseSource Source => (FakePoseSource)Environment.PoseSource;

    public FakeFocus Focus => (FakeFocus)Environment.Focus;

    public FakeDisplayCatalog Displays => (FakeDisplayCatalog)Environment.Displays;

    public FakeCameraCatalog Cameras => (FakeCameraCatalog)Environment.Cameras;

    public FakeSessionEvents Session => (FakeSessionEvents)Environment.Session;

    public FakeSettingsStore Settings => (FakeSettingsStore)Environment.Settings;

    public FakeHotKeys HotKeys => (FakeHotKeys)Environment.HotKeys;

    public FakeMainThread Main => (FakeMainThread)Environment.MainThread;

    public ManualDelayScheduler Scheduler => (ManualDelayScheduler)Environment.Scheduler;

    public FakeSwitchEngine Engine => (FakeSwitchEngine)Environment.Engine;

    /// <summary>The monotonic clock the controller reads (hold timing). <see cref="Advance"/> moves it with the scheduler.</summary>
    public TestClock Clock { get; }

    public int StateChangedCount { get; private set; }

    /// <summary>The default environment: fakes, a scripted engine, no repeating poll.</summary>
    public static Harness Make(
        CameraAuthorization authorization = CameraAuthorization.Authorized,
        DisplayLayout? layout = null,
        bool autoRun = false,
        string? savedCameraId = null,
        bool calibrated = false,
        string?[]? calibrationCameras = null,
        CameraDevice? capturePicks = null,
        bool trackingSaved = false,
        bool sessionInactive = false,
        double? focusPollInterval = null,
        FakeSettingsStore? settingsStore = null,
        FakeHotKeys? hotKeys = null,
        Action<FakeSettingsStore>? seed = null)
    {
        Inline.Ensure();
        layout ??= Fixtures.TwoDisplays;
        var settings = settingsStore ?? new FakeSettingsStore();
        if (savedCameraId is not null)
        {
            settings.Values[SettingsKeys.CameraDeviceId] = savedCameraId;
        }

        if (calibrated)
        {
            var cameras = calibrationCameras ?? [Fixtures.BuiltInCamera.Name];
            SeedLibrary(settings, cameras.Select(c => Fixtures.Profile(layout, c)).ToArray());
        }

        if (trackingSaved)
        {
            settings.Values[SettingsKeys.TrackingEnabled] = "true";
        }

        seed?.Invoke(settings);
        var clock = new TestClock();
        var environment = new TrackingEnvironment
        {
            PoseSource = new FakePoseSource { AutoRunDevice = capturePicks ?? (autoRun ? Fixtures.BuiltInCamera : null) },
            Focus = new FakeFocus(),
            Displays = new FakeDisplayCatalog(layout),
            Cameras = new FakeCameraCatalog { AuthorizationValue = authorization },
            Session = new FakeSessionEvents { InactiveNow = sessionInactive },
            Settings = settings,
            HotKeys = hotKeys ?? new FakeHotKeys(),
            MainThread = new FakeMainThread(),
            Scheduler = new ManualDelayScheduler(),
            Clock = () => clock.Now,
            Now = () => DateTimeOffset.FromUnixTimeSeconds(1_700_000_000),
            Engine = new FakeSwitchEngine(),
            FocusPollInterval = focusPollInterval,
        };
        var controller = new TrackingController(environment);
        var harness = new Harness(environment, controller, clock);
        controller.StateChanged += () => harness.StateChangedCount++;
        harness.Drain();
        return harness;
    }

    public static void SeedLibrary(FakeSettingsStore settings, params CalibrationProfile[] profiles) =>
        settings.Values[SettingsKeys.CalibrationLibrary] = JsonSerializer.Serialize(new CalibrationLibrary(profiles), EngineJson.Options);

    /// <summary>The library as saved in the settings, or null when absent.</summary>
    public CalibrationLibrary? StoredLibrary() =>
        Settings.Values.TryGetValue(SettingsKeys.CalibrationLibrary, out var json)
            ? JsonSerializer.Deserialize<CalibrationLibrary>(json, EngineJson.Options)
            : null;

    /// <summary>A second harness on the same settings, as after a relaunch (optionally on another layout).</summary>
    public Harness Relaunch(DisplayLayout? layout = null, bool autoRun = false, FakeHotKeys? hotKeys = null) =>
        Make(layout: layout ?? Fixtures.TwoDisplays, autoRun: autoRun, hotKeys: hotKeys, settingsStore: Settings);

    public void Drain()
    {
        Inline.Ensure();
        Main.Drain();
    }

    /// <summary>Moves the scheduler and the clock forward in small steps, draining the main thread after each so chained timers run.</summary>
    public void Advance(double seconds)
    {
        Inline.Ensure();
        const double step = 0.0625; // Exactly representable, so the clock sums stay exact.
        var remaining = seconds;
        while (remaining > 1e-9)
        {
            var dt = Math.Min(step, remaining);
            Scheduler.Advance(dt);
            Clock.Advance(dt);
            Drain();
            remaining -= dt;
        }
    }

    public void Emit(PoseSourceEvent sourceEvent)
    {
        Inline.Ensure();
        Source.Emit(sourceEvent);
        Drain();
    }

    /// <summary>Offers one sample stamped with <paramref name="time"/>.</summary>
    public void Feed(double time, bool face = true, double yaw = 0, double pitch = 0) =>
        Emit(new PoseSourceEvent.Sample(Fixtures.Sample(time, face, yaw, pitch)));

    /// <summary>Offers several samples before the main thread runs anything, as a burst of frames would arrive.</summary>
    public void FeedBurst(params double[] times)
    {
        foreach (var time in times)
        {
            Source.EmitSample(Fixtures.Sample(time));
        }

        Drain();
    }

    /// <summary>Reports the camera as running, as the live pose source would.</summary>
    public void ReportRunning(CameraDevice? device = null) =>
        Emit(new PoseSourceEvent.Status(new CaptureStatus.Running(device ?? Fixtures.BuiltInCamera)));

    public void ReportStatus(CaptureStatus status) => Emit(new PoseSourceEvent.Status(status));

    public async Task StartTracking()
    {
        Inline.Ensure();
        await Controller.SetTrackingEnabledAsync(true);
        Drain();
        ReportRunning();
    }

    public async Task SetTracking(bool enabled)
    {
        Inline.Ensure();
        await Controller.SetTrackingEnabledAsync(enabled);
        Drain();
    }

    public void RaiseSession(SessionEvent sessionEvent)
    {
        Session.Raise(sessionEvent);
        Drain();
    }

    public void Dispose() => Controller.Dispose();
}
