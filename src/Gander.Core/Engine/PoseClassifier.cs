namespace Gander.Core;

/// <summary>
/// Classifies one pose against a calibration profile (CONTRACT section "Classification").
/// The decision is nearest calibrated display center in axis-scale units (SPEC D18), with an "elsewhere" box around
/// all display centers for looks at the keyboard or away. The constructor precomputes the box.
/// </summary>
public sealed class PoseClassifier
{
    private readonly CalibrationProfile _profile;
    private readonly SwitchSettings _settings;
    private readonly double _minYaw;
    private readonly double _maxYaw;
    private readonly double _minPitch;
    private readonly double _maxPitch;

    /// <summary>Creates a classifier.</summary>
    public PoseClassifier(CalibrationProfile profile, SwitchSettings settings)
    {
        _profile = profile;
        _settings = settings;
        var yaws = profile.Displays.Select(d => d.Yaw).ToList();
        var pitches = profile.Displays.Select(d => d.Pitch).ToList();
        _minYaw = (yaws.Count == 0 ? 0 : yaws.Min()) - settings.ElsewhereYawMargin;
        _maxYaw = (yaws.Count == 0 ? 0 : yaws.Max()) + settings.ElsewhereYawMargin;
        _minPitch = (pitches.Count == 0 ? 0 : pitches.Min()) - settings.ElsewherePitchMargin;
        _maxPitch = (pitches.Count == 0 ? 0 : pitches.Max()) + settings.ElsewherePitchMargin;
    }

    /// <summary>
    /// Classifies the pose (null means no face). A profile with no displays gives NoFace; a single display gives no
    /// runner-up and margin 0, so it is never confident enough to switch.
    /// </summary>
    public PoseClassification Classify(HeadPose? pose)
    {
        // Step 1: no usable face.
        if (pose is not { } p || p.FaceConfidence < _settings.MinFaceConfidence
            || _profile.Features.PointOf(p) is not { } point || _profile.Displays.Count == 0)
        {
            return PoseClassification.NoFace;
        }

        // Step 2: normalized distance to every display.
        var displays = _profile.Displays;
        var distances = new double[displays.Count];
        var byDisplay = new Dictionary<DisplayId, double>();
        for (var i = 0; i < displays.Count; i++)
        {
            var dx = (point.X - displays[i].Yaw) / _profile.YawScale;
            var dy = (point.Y - displays[i].Pitch) / _profile.PitchScale;
            distances[i] = Math.Sqrt((dx * dx) + (dy * dy));
            byDisplay[displays[i].DisplayId] = distances[i];
        }

        // Step 3: best and runner-up. Strict comparisons keep the earlier display on ties.
        var best = 0;
        int? runnerUp = null;
        for (var index = 1; index < distances.Length; index++)
        {
            if (distances[index] < distances[best])
            {
                runnerUp = best;
                best = index;
            }
            else if (runnerUp is { } current)
            {
                if (distances[index] < distances[current])
                {
                    runnerUp = index;
                }
            }
            else
            {
                runnerUp = index;
            }
        }

        var bestId = displays[best].DisplayId;
        DisplayId? runnerUpId = runnerUp is { } r ? displays[r].DisplayId : (DisplayId?)null;
        var margin = 0.0;
        if (runnerUp is { } second)
        {
            var sum = distances[second] + distances[best];
            margin = sum == 0 ? 0 : (distances[second] - distances[best]) / sum;
        }

        // Step 4: outside the box around all display centers.
        if (point.X < _minYaw || point.X > _maxYaw || point.Y < _minPitch || point.Y > _maxPitch)
        {
            return new PoseClassification(PoseClassification.ClassKind.Elsewhere, bestId, runnerUpId, 0, byDisplay);
        }

        // Step 5.
        return new PoseClassification(PoseClassification.ClassKind.Display, bestId, runnerUpId, margin, byDisplay);
    }
}
