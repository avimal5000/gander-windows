using Sway.Core.Platform;

namespace Sway.Core.Controller;

/// <summary>
/// Counters and the latest pose for the diagnostics, so "why is it not switching" can be read off a copied report or
/// the log file. Additive bookkeeping: nothing in the tracking loop reads it. Numbers only (no images, no titles).
/// Updated on the main thread by the controller.
/// </summary>
public sealed class SwitchDiagnostics
{
    /// <summary>Pose samples the camera delivered, with or without a face, since Sway started or Tracking was turned on.</summary>
    public int SamplesReceived { get; private set; }

    /// <summary>Samples that carried a face pose.</summary>
    public int SamplesWithFace { get; private set; }

    /// <summary>Samples the switch decider processed (Tracking active and the camera running).</summary>
    public int SamplesProcessed { get; private set; }

    /// <summary>Times the decider asked for a switch.</summary>
    public int SwitchRequests { get; private set; }

    /// <summary>Requests skipped because a hold was active.</summary>
    public int SkippedByHold { get; private set; }

    /// <summary>Requests skipped because another attempt was still in flight.</summary>
    public int SkippedInFlight { get; private set; }

    /// <summary>Requests that ended with a window focused.</summary>
    public int Focused { get; private set; }

    /// <summary>Requests that found no window on the display.</summary>
    public int NoWindow { get; private set; }

    /// <summary>Requests that failed.</summary>
    public int Failed { get; private set; }

    /// <summary>The latest pose that had a face; kept while later samples have none.</summary>
    public HeadPose? LastPose { get; private set; }

    /// <summary>When <see cref="LastPose"/> arrived.</summary>
    public DateTimeOffset? LastPoseAt { get; private set; }

    /// <summary>When the latest sample arrived, with a face or not.</summary>
    public DateTimeOffset? LastSampleAt { get; private set; }

    /// <summary>Whether the latest sample had a face.</summary>
    public bool LastSampleHadFace { get; private set; }

    /// <summary>Counts one pose sample and keeps its pose.</summary>
    public void RecordSample(HeadPose? pose, DateTimeOffset now)
    {
        SamplesReceived++;
        LastSampleAt = now;
        LastSampleHadFace = pose is not null;
        if (pose is { } face)
        {
            SamplesWithFace++;
            LastPose = face;
            LastPoseAt = now;
        }
    }

    /// <summary>Counts a sample that went through the decider.</summary>
    public void RecordProcessed() => SamplesProcessed++;

    /// <summary>Counts a switch the decider asked for.</summary>
    public void RecordRequest() => SwitchRequests++;

    /// <summary>Counts a request dropped because a hold was active.</summary>
    public void RecordSkippedByHold() => SkippedByHold++;

    /// <summary>Counts a request dropped because an attempt was already running.</summary>
    public void RecordSkippedInFlight() => SkippedInFlight++;

    /// <summary>Counts the result of a focus request.</summary>
    public void RecordResult(FocusResult result)
    {
        switch (result)
        {
            case FocusResult.Focused:
                Focused++;
                break;
            case FocusResult.NoWindow:
                NoWindow++;
                break;
            case FocusResult.Failed:
                Failed++;
                break;
        }
    }

    /// <summary>Zeroes the counters (Tracking was turned on). The latest pose is kept.</summary>
    public void ResetCounts()
    {
        SamplesReceived = 0;
        SamplesWithFace = 0;
        SamplesProcessed = 0;
        SwitchRequests = 0;
        SkippedByHold = 0;
        SkippedInFlight = 0;
        Focused = 0;
        NoWindow = 0;
        Failed = 0;
    }

    /// <summary>The counters on one line, for the log.</summary>
    public string CountsLine() =>
        $"samples {SamplesReceived}, face {SamplesWithFace}, processed {SamplesProcessed}, requests {SwitchRequests} "
        + $"(focused {Focused}, no window {NoWindow}, failed {Failed}), skipped by hold {SkippedByHold}, skipped in flight {SkippedInFlight}";
}
