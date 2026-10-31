using Gander.Core.Platform;
using Gander.Core.Runtime;

namespace Gander.Core.Controller;

/// <summary>
/// Everything the <see cref="TrackingController"/> talks to (Mac AppModelEnvironment plus the injected protocols).
/// Tests supply fakes; the app supplies the Windows adapters, a dispatcher-backed <see cref="IMainThread"/>, and
/// <see cref="TimerDelayScheduler"/>.
/// </summary>
public sealed record TrackingEnvironment
{
    /// <summary>The live camera pose stream.</summary>
    public required IPoseSource PoseSource { get; init; }

    /// <summary>Last-used windows, the focused display, and focus requests.</summary>
    public required IFocusController Focus { get; init; }

    /// <summary>The monitors and their changes.</summary>
    public required IDisplayCatalog Displays { get; init; }

    /// <summary>Cameras and camera permission.</summary>
    public required ICameraCatalog Cameras { get; init; }

    /// <summary>Lock, sleep and session events.</summary>
    public required ISessionEvents Session { get; init; }

    /// <summary>Where the user's choices and the calibration library are saved.</summary>
    public required ISettingsStore Settings { get; init; }

    /// <summary>Registers the Focus Lock shortcut.</summary>
    public required IHotKeyRegistrar HotKeys { get; init; }

    /// <summary>The thread that owns the controller.</summary>
    public required IMainThread MainThread { get; init; }

    /// <summary>Runs the one-shot delays (holds, grace, device list refresh, camera wait and retries).</summary>
    public required IDelayScheduler Scheduler { get; init; }

    /// <summary>Monotonic seconds, used for the hold timing.</summary>
    public required Func<double> Clock { get; init; }

    /// <summary>Wall-clock time stamped on switch log rows.</summary>
    public Func<DateTimeOffset> Now { get; init; } = () => DateTimeOffset.UtcNow;

    /// <summary>Fits profiles and makes deciders. Tests script it.</summary>
    public ISwitchEngineMaking Engine { get; init; } = new PoseSwitchEngine();

    /// <summary>
    /// Seconds between focused-display polls while tracking runs (SPEC D29); null disables the repeating poll (tests
    /// call <see cref="TrackingController.TrackingTick"/> instead).
    /// </summary>
    public double? FocusPollInterval { get; init; } = 1;

    /// <summary>True when the main display is asleep, for the suspend tracker's self-heal of a missed display wake.</summary>
    public Func<bool> IsMainDisplayAsleep { get; init; } = () => false;

    /// <summary>Log sink (never receives a window title). Null logs nothing.</summary>
    public Action<string>? Log { get; init; }
}
