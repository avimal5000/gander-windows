using System.Threading.Channels;
using Sway.Core.Controller;
using Sway.Core.Platform;
using Sway.Core.Runtime;

namespace Sway.Core.Tests.Controller;

/// <summary>
/// xunit runs tests under its own SynchronizationContext, and a task only runs its continuations inline when the
/// completing thread has none. The controller's tests need them inline (the dispatcher thread they stand for), so the
/// fakes complete held tasks through this.
/// </summary>
internal static class Inline
{
    /// <summary>A context that runs posted work at once, so continuations captured by the controller stay on the test thread.</summary>
    private sealed class InlineContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => d(state);

        public override void Send(SendOrPostCallback d, object? state) => d(state);

        public override SynchronizationContext CreateCopy() => this;
    }

    private static readonly InlineContext Shared = new();

    /// <summary>Makes the calling thread's context the inline one (the dispatcher thread the controller expects).</summary>
    public static void Ensure()
    {
        if (SynchronizationContext.Current != Shared)
        {
            SynchronizationContext.SetSynchronizationContext(Shared);
        }
    }

    public static void Run(Action action)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            action();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }
}

/// <summary>A main thread that queues posted work until the test drains it (like a dispatcher, minus the thread).</summary>
public sealed class FakeMainThread : IMainThread
{
    private readonly Queue<Action> _queue = new();

    public int PostCount { get; private set; }

    public void Post(Action action)
    {
        PostCount++;
        lock (_queue)
        {
            _queue.Enqueue(action);
        }
    }

    /// <summary>Runs everything posted, including work posted by that work.</summary>
    public void Drain()
    {
        while (true)
        {
            Action? next;
            lock (_queue)
            {
                next = _queue.Count > 0 ? _queue.Dequeue() : null;
            }

            if (next is null)
            {
                return;
            }

            next();
        }
    }
}

/// <summary>A manually advanced monotonic clock.</summary>
public sealed class TestClock
{
    public double Now { get; private set; }

    public void Advance(double seconds) => Now += seconds;

    public void Set(double seconds) => Now = seconds;
}

/// <summary>A pose source whose event stream the test controls. Completes everything inline.</summary>
public sealed class FakePoseSource : IPoseSource
{
    public sealed record DemandCall(PoseDemand Demand, int StartsSoFar);

    private readonly List<PoseSourceConfiguration> _configurations = [];
    private readonly List<DemandCall> _demandCalls = [];
    private readonly Queue<CameraError> _queuedStartErrors = new();
    private Channel<PoseSourceEvent>? _channel;

    /// <summary>When set, every start reports Starting and Running(device); a known id picks that camera.</summary>
    public CameraDevice? AutoRunDevice { get; set; }

    public CameraError? StartError { get; set; }

    public Exception? ThrowOnStart { get; set; }

    public int StopCount { get; private set; }

    public IReadOnlyList<PoseSourceConfiguration> Configurations => _configurations;

    public int StartCount => _configurations.Count;

    public bool IsStreaming => _channel is not null;

    public IReadOnlyList<DemandCall> DemandCalls => _demandCalls;

    public IReadOnlyList<PoseDemand> Demands => _demandCalls.Select(c => c.Demand).ToList();

    /// <summary>Makes the next starts throw these errors in order; starts after them succeed.</summary>
    public void FailNextStarts(params CameraError[] errors)
    {
        foreach (var error in errors)
        {
            _queuedStartErrors.Enqueue(error);
        }
    }

    public Task<IAsyncEnumerable<PoseSourceEvent>> StartAsync(PoseSourceConfiguration configuration, CancellationToken cancellationToken = default)
    {
        _configurations.Add(configuration);
        if (ThrowOnStart is { } thrown)
        {
            return Task.FromException<IAsyncEnumerable<PoseSourceEvent>>(thrown);
        }

        var error = StartError ?? (_queuedStartErrors.Count > 0 ? _queuedStartErrors.Dequeue() : null);
        if (error is not null)
        {
            return Task.FromException<IAsyncEnumerable<PoseSourceEvent>>(new CameraStartException(error));
        }

        _channel?.Writer.TryComplete();
        var channel = Channel.CreateUnbounded<PoseSourceEvent>(new UnboundedChannelOptions { AllowSynchronousContinuations = true });
        _channel = channel;
        if (AutoRunDevice is { } fallback)
        {
            var known = new[] { Fixtures.BuiltInCamera, Fixtures.ExternalCamera, Fixtures.SecondExternalCamera };
            var device = known.FirstOrDefault(d => d.Id == configuration.CameraDeviceId) ?? fallback;
            channel.Writer.TryWrite(new PoseSourceEvent.Status(new CaptureStatus.Starting()));
            channel.Writer.TryWrite(new PoseSourceEvent.Status(new CaptureStatus.Running(device)));
        }

        return Task.FromResult<IAsyncEnumerable<PoseSourceEvent>>(channel.Reader.ReadAllAsync());
    }

    public void SetDemand(PoseDemand demand) => _demandCalls.Add(new DemandCall(demand, _configurations.Count));

    public Task StopAsync()
    {
        StopCount++;
        var channel = _channel;
        _channel = null;
        channel?.Writer.TryWrite(new PoseSourceEvent.Status(new CaptureStatus.Stopped()));
        channel?.Writer.TryComplete();
        return Task.CompletedTask;
    }

    public void Emit(PoseSourceEvent sourceEvent) => _channel?.Writer.TryWrite(sourceEvent);

    public void EmitStatus(CaptureStatus status) => Emit(new PoseSourceEvent.Status(status));

    public void EmitSample(TimedPose sample) => Emit(new PoseSourceEvent.Sample(sample));

    /// <summary>Ends the stream without a StopAsync call, as a crashing source would.</summary>
    public void FinishStream()
    {
        _channel?.Writer.TryComplete();
        _channel = null;
    }
}

/// <summary>A focus controller whose requests can be held open so a test sees one in flight.</summary>
public sealed class FakeFocus : IFocusController
{
    private TaskCompletionSource<bool>? _hold;
    private bool _holding;

    public List<DisplayId> FocusCalls { get; } = [];

    public List<DisplayLayout> FocusLayouts { get; } = [];

    public int FocusedDisplayCalls { get; private set; }

    public DisplayId? FocusedDisplay { get; set; }

    public FocusResult Result { get; set; } = new FocusResult.NoWindow("none");

    public bool IsHoldingFocus => _hold is { Task.IsCompleted: false };

    /// <summary>Holds the focused-display poll answer until <see cref="ReleasePoll"/>.</summary>
    public void HoldPolls() => _pollHold = new TaskCompletionSource<bool>();

    private TaskCompletionSource<bool>? _pollHold;

    public void ReleasePoll() => Inline.Run(() => _pollHold?.SetResult(true));

    public void HoldFocusRequests()
    {
        _holding = true;
        _hold = new TaskCompletionSource<bool>();
    }

    public void Release()
    {
        _holding = false;
        Inline.Run(() => _hold?.TrySetResult(true));
    }

    public Task<IReadOnlyDictionary<DisplayId, WindowSummary>> LastUsedWindowsAsync(DisplayLayout layout) =>
        Task.FromResult<IReadOnlyDictionary<DisplayId, WindowSummary>>(new Dictionary<DisplayId, WindowSummary>());

    public async Task<DisplayId?> FocusedDisplayAsync(DisplayLayout layout)
    {
        FocusedDisplayCalls++;
        if (_pollHold is { } hold)
        {
            await hold.Task.ConfigureAwait(false);
        }

        return FocusedDisplay;
    }

    public async Task<FocusResult> FocusAsync(DisplayId display, DisplayLayout layout)
    {
        FocusCalls.Add(display);
        FocusLayouts.Add(layout);
        if (_holding && _hold is { } hold)
        {
            await hold.Task.ConfigureAwait(false);
        }

        return Result;
    }
}

public sealed class FakeDisplayCatalog(DisplayLayout layout) : IDisplayCatalog
{
    public DisplayLayout Layout { get; set; } = layout;

    public event Action? LayoutChanged;

    public DisplayLayout Current() => Layout;

    public void Change(DisplayLayout newLayout)
    {
        Layout = newLayout;
        LayoutChanged?.Invoke();
    }

    /// <summary>Raises the event without changing the layout (a notification that changes nothing).</summary>
    public void Notify() => LayoutChanged?.Invoke();
}

public sealed class FakeCameraCatalog : ICameraCatalog
{
    public CameraAuthorization AuthorizationValue { get; set; } = CameraAuthorization.Authorized;

    public List<CameraDevice> Devices { get; set; } = [Fixtures.BuiltInCamera, Fixtures.ExternalCamera];

    public event Action? DevicesChanged;

    public CameraAuthorization Authorization() => AuthorizationValue;

    public int DeviceListCalls { get; private set; }

    public IReadOnlyList<CameraDevice> AvailableDevices()
    {
        DeviceListCalls++;
        return Devices.ToList();
    }

    /// <summary>The device for a saved id, else the built-in, else the first (the catalog's own rule).</summary>
    public CameraDevice? Resolve(string? deviceId) =>
        Devices.FirstOrDefault(d => d.Id == deviceId) ?? Devices.FirstOrDefault(d => d.IsBuiltIn) ?? Devices.FirstOrDefault();

    public void Plug(CameraDevice device)
    {
        Devices.Add(device);
        DevicesChanged?.Invoke();
    }

    public void Unplug(string id)
    {
        Devices.RemoveAll(d => d.Id == id);
        DevicesChanged?.Invoke();
    }

    public void NotifyOnly() => DevicesChanged?.Invoke();
}

public sealed class FakeSessionEvents : ISessionEvents
{
    public bool InactiveNow { get; set; }

    public event Action<SessionEvent>? Occurred;

    public bool IsSessionInactiveNow() => InactiveNow;

    public void Raise(SessionEvent sessionEvent) => Occurred?.Invoke(sessionEvent);
}

public sealed class FakeSettingsStore : ISettingsStore
{
    public Dictionary<string, string> Values { get; } = [];

    public int WriteCount { get; private set; }

    public string? Read(string key) => Values.GetValueOrDefault(key);

    public void Write(string key, string value)
    {
        WriteCount++;
        Values[key] = value;
    }

    public void Remove(string key) => Values.Remove(key);
}

/// <summary>Like the real registry, Register first drops the action's current shortcut, so a failed registration leaves none.</summary>
public sealed class FakeHotKeys : IHotKeyRegistrar
{
    private readonly Dictionary<HotKeyAction, Action> _handlers = [];

    public Dictionary<HotKeyAction, HotKeyShortcut> Registered { get; } = [];

    public List<HotKeyShortcut> RegisterCalls { get; } = [];

    public List<HotKeyAction> UnregisterCalls { get; } = [];

    public List<(HotKeyShortcut Shortcut, HotKeyException Error)> Failures { get; } = [];

    public void Register(HotKeyShortcut shortcut, HotKeyAction action, Action handler)
    {
        RegisterCalls.Add(shortcut);
        Registered.Remove(action);
        _handlers.Remove(action);
        if (Failures.FirstOrDefault(f => f.Shortcut == shortcut) is { Error: { } error })
        {
            throw error;
        }

        Registered[action] = shortcut;
        _handlers[action] = handler;
    }

    public void Unregister(HotKeyAction action)
    {
        UnregisterCalls.Add(action);
        Registered.Remove(action);
        _handlers.Remove(action);
    }

    public void Press(HotKeyAction action = HotKeyAction.ToggleFocusLock)
    {
        if (_handlers.TryGetValue(action, out var handler))
        {
            handler();
        }
    }
}

/// <summary>A decider that answers from a script and records what it was asked.</summary>
public sealed class DecisionScript
{
    public Func<TimedPose, SwitchContext, SwitchDecision> Rule { get; set; } = (_, _) => Fixtures.Decision();
}

public sealed class DeciderRecorder(CalibrationProfile profile, SwitchSettings settings)
{
    public CalibrationProfile Profile { get; } = profile;

    public SwitchSettings Settings { get; } = settings;

    public List<TimedPose> Samples { get; } = [];

    public List<SwitchContext> Contexts { get; } = [];

    public int ResetCount { get; set; }
}

public sealed class FakeSwitchEngine : ISwitchEngineMaking
{
    public DecisionScript Script { get; } = new();

    public List<DeciderRecorder> Recorders { get; } = [];

    public DeciderRecorder? LastRecorder => Recorders.LastOrDefault();

    public CalibrationProfile FitProfile(LabeledPoseTrace trace, PoseFeatureSet features, IReadOnlySet<int>? rounds) =>
        throw new NotSupportedException();

    public PoseClassification Classify(HeadPose? pose, CalibrationProfile profile, SwitchSettings settings) => PoseClassification.NoFace;

    public ISwitchDecider MakeDecider(CalibrationProfile profile, SwitchSettings settings)
    {
        var recorder = new DeciderRecorder(profile, settings);
        Recorders.Add(recorder);
        return new FakeDecider(recorder, Script);
    }

    private sealed class FakeDecider(DeciderRecorder recorder, DecisionScript script) : ISwitchDecider
    {
        public SwitchDecision Process(TimedPose sample, SwitchContext context)
        {
            recorder.Samples.Add(sample);
            recorder.Contexts.Add(context);
            return script.Rule(sample, context);
        }

        public void Reset() => recorder.ResetCount++;
    }
}
