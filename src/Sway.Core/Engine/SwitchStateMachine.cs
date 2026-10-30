namespace Sway.Core;

/// <summary>
/// The switch decider: classifies each sample and runs the dwell / cooldown state machine (CONTRACT section
/// "SwitchStateMachine", SPEC F14). Pure and deterministic. Time comes only from <see cref="TimedPose.Timestamp"/>, so
/// a recorded trace replays to the same switches every time. Step numbers in the comments match the contract.
/// </summary>
public sealed class SwitchStateMachine : ISwitchDecider
{
    /// <summary>
    /// Epsilon of the contract, seconds. Timestamps can be large (host uptime), where a double resolves only about
    /// 6e-11 s, so the difference of two stamps exactly 0.4 s apart can compute as 0.39999999996. Every time comparison
    /// allows this much in the direction that meets its threshold, so a boundary frame is never one frame late.
    /// </summary>
    public const double TimeTolerance = 1e-6;

    private readonly PoseClassifier _classifier;
    private readonly SwitchSettings _settings;
    private State _state = State.Idle;
    private double? _lastTimestamp;

    /// <summary>Creates a decider bound to a profile and settings, in the idle phase.</summary>
    public SwitchStateMachine(CalibrationProfile profile, SwitchSettings settings)
    {
        _classifier = new PoseClassifier(profile, settings);
        _settings = settings;
    }

    private enum Mode
    {
        Idle,
        Dwelling,
        CoolingDown,
    }

    /// <inheritdoc />
    public void Reset()
    {
        _state = State.Idle;
        _lastTimestamp = null;
    }

    /// <inheritdoc />
    public SwitchDecision Process(TimedPose sample, SwitchContext context)
    {
        var t = sample.Timestamp;

        // Step 0: time went backwards, so this is a new stream.
        if (_lastTimestamp is { } last && t < last)
        {
            Reset();
        }

        _lastTimestamp = t;

        // Step 1.
        var classification = _classifier.Classify(sample.Pose);

        // Step 2: focus lock cancels everything, including a cooldown.
        if (context.IsFocusLocked)
        {
            _state = State.Idle;
            return Decision(SwitchPhase.Locked, SwitchHoldReason.FocusLocked, classification);
        }

        // Step 3: cooldown. It is over when t >= until - epsilon.
        if (_state.Mode == Mode.CoolingDown)
        {
            var until = _state.Until;
            if (t < until - TimeTolerance)
            {
                return Decision(SwitchPhase.CoolingDown(until), SwitchHoldReason.CoolingDown, classification);
            }

            _state = State.Idle;
        }

        // Step 4: an ongoing dwell may fire, continue, or be dropped (and then fall through to step 5).
        if (_state.Mode == Mode.Dwelling
            && ContinueDwell(_state.Display, _state.Start, _state.LastSupport, t, classification, context) is { } result)
        {
            return result;
        }

        // Step 5: idle.
        return ProcessIdle(t, classification, context);
    }

    // Step 4. Returns null when the dwell ended without a decision and the sample should be handled as idle
    // (steps 4(a), 4(a2), and 4(c)).
    private SwitchDecision? ContinueDwell(
        DisplayId target, double start, double lastSupport, double t, PoseClassification classification, SwitchContext context)
    {
        // (a) Focus already moved to the dwell target (for example by the user).
        if (context.FocusedDisplay == target)
        {
            _state = State.Idle;
            return null;
        }

        // (a2) Too long since the last supporting sample: give up. Covers a run of non-supporting samples and a hole
        // in the stream alike, so a stale dwell can never fire.
        if (t - lastSupport > _settings.MaxGap + TimeTolerance)
        {
            _state = State.Idle;
            return null;
        }

        // (b) The look is still supported at the lower stay margin.
        if (classification.Kind == PoseClassification.ClassKind.Display && classification.Best == target
            && classification.Margin >= _settings.StayMargin && IsTarget(target, context))
        {
            _state = State.Dwelling(target, start, t);
            if (IsDwellComplete(t - start))
            {
                return Fire(target, t, classification);
            }

            return Decision(SwitchPhase.Dwelling(target, Progress(start, t)), SwitchHoldReason.Dwelling, classification);
        }

        // (c) A confident look at another display replaces the dwell (step 5 may start a new one).
        if (EligibleDisplay(classification, _settings.EnterMargin, context) is { } other && other != target)
        {
            _state = State.Idle;
            return null;
        }

        // (e) A short gap (at most maxGap): the dwell clock keeps running, but only a supporting sample can fire.
        return Decision(SwitchPhase.Dwelling(target, Progress(start, t)), SwitchHoldReason.Dwelling, classification);
    }

    // Step 5.
    private SwitchDecision ProcessIdle(double t, PoseClassification classification, SwitchContext context)
    {
        if (HoldReason(classification, context) is { } reason)
        {
            return Decision(SwitchPhase.Idle, reason, classification);
        }

        // No hold reason means a display with enough margin, so Best is set; the check only avoids a null deref.
        if (classification.Best is not { } best)
        {
            return Decision(SwitchPhase.Idle, SwitchHoldReason.NoFace, classification);
        }

        if (IsDwellComplete(0))
        {
            // dwell <= 0 (within epsilon): nothing to wait for
            return Fire(best, t, classification);
        }

        _state = State.Dwelling(best, t, t);
        return Decision(SwitchPhase.Dwelling(best, 0), SwitchHoldReason.Dwelling, classification);
    }

    /// <summary>Why an idle decider does not start a dwell on this sample, in the contract's order, or null when it would.</summary>
    private SwitchHoldReason? HoldReason(PoseClassification classification, SwitchContext context)
    {
        switch (classification.Kind)
        {
            case PoseClassification.ClassKind.NoFace:
                return SwitchHoldReason.NoFace;
            case PoseClassification.ClassKind.Elsewhere:
                return SwitchHoldReason.Elsewhere;
            default:
                if (classification.Best is not { } best)
                {
                    return SwitchHoldReason.NoFace;
                }

                if (!IsTarget(best, context))
                {
                    return SwitchHoldReason.NotATarget;
                }

                if (best == context.FocusedDisplay)
                {
                    return SwitchHoldReason.AlreadyFocused;
                }

                return classification.Margin < _settings.EnterMargin ? SwitchHoldReason.Uncertain : null;
        }
    }

    /// <summary>inTargets(D) from the contract: no target set means every calibrated display.</summary>
    private static bool IsTarget(DisplayId display, SwitchContext context) =>
        context.Targets is null || context.Targets.Contains(display);

    /// <summary>The best display when eligible(c, m) from the contract holds, else null.</summary>
    private static DisplayId? EligibleDisplay(PoseClassification classification, double margin, SwitchContext context) =>
        classification.Kind == PoseClassification.ClassKind.Display && classification.Margin >= margin
        && classification.Best is { } best && IsTarget(best, context)
            ? best
            : (DisplayId?)null;

    /// <summary>A dwell is complete when elapsed &gt;= dwell - epsilon.</summary>
    private bool IsDwellComplete(double elapsed) => elapsed >= _settings.Dwell - TimeTolerance;

    private double Progress(double start, double t) =>
        _settings.Dwell > 0 ? Math.Min(1, (t - start) / _settings.Dwell) : 1;

    /// <summary>Completes a dwell: enters the cooldown and tells the caller to switch.</summary>
    private SwitchDecision Fire(DisplayId target, double t, PoseClassification classification)
    {
        var until = t + _settings.Cooldown;
        _state = State.CoolingDown(until);
        return new SwitchDecision(target, SwitchPhase.CoolingDown(until), null, classification);
    }

    private static SwitchDecision Decision(SwitchPhase phase, SwitchHoldReason reason, PoseClassification classification) =>
        new(null, phase, reason, classification);

    private readonly record struct State(Mode Mode, DisplayId Display, double Start, double LastSupport, double Until)
    {
        public static State Idle => new(Mode.Idle, default, 0, 0, 0);

        public static State Dwelling(DisplayId display, double start, double lastSupport) =>
            new(Mode.Dwelling, display, start, lastSupport, 0);

        public static State CoolingDown(double until) => new(Mode.CoolingDown, default, 0, 0, until);
    }
}
