namespace Sway.Core;

/// <summary>
/// Fits a <see cref="CalibrationProfile"/> from a labeled trace (CONTRACT section "Fitting"). Pure and deterministic:
/// the same trace, features, and rounds always give an equal profile, because CreatedAt comes from the trace and every
/// pass over the data runs in a fixed order.
/// </summary>
public sealed class ProfileFitter
{
    /// <summary>Lowest yaw scale, degrees. Keeps a very still calibration from making every wobble look large.</summary>
    public const double YawScaleFloor = 1.5;

    /// <summary>Lowest pitch scale, degrees.</summary>
    public const double PitchScaleFloor = 2.5;

    /// <summary>The default for <see cref="MinPointsPerDisplay"/> (the Mac gate's minFaceSamplesPerDisplay).</summary>
    public const int DefaultMinPointsPerDisplay = 30;

    /// <summary>Creates a fitter.</summary>
    public ProfileFitter(int minPointsPerDisplay = DefaultMinPointsPerDisplay)
    {
        MinPointsPerDisplay = minPointsPerDisplay;
    }

    /// <summary>A display needs at least this many face samples (with the requested features) to be profiled.</summary>
    public int MinPointsPerDisplay { get; }

    /// <summary>Fits a profile. Unlabeled segments are ignored; rounds null uses every round.</summary>
    /// <exception cref="CalibrationException">FeaturesMissing when the used segments have faces but none with the
    /// features; TooFewDisplays when fewer than two displays reach the minimum (including no faces at all).</exception>
    public CalibrationProfile Fit(LabeledPoseTrace trace, PoseFeatureSet features, IReadOnlySet<int>? rounds)
    {
        var firstAppearance = new List<DisplayId>();
        var pointsByDisplay = new Dictionary<DisplayId, List<FeaturePoint>>();
        var faceSampleCount = 0;

        // Steps 1 and 2: the feature points of every labeled sample in the used segments, grouped by display.
        foreach (var segment in trace.Segments)
        {
            if (segment.DisplayId is not { } id)
            {
                continue;
            }

            if (rounds is not null && !rounds.Contains(segment.Round))
            {
                continue;
            }

            if (!pointsByDisplay.TryGetValue(id, out var points))
            {
                firstAppearance.Add(id);
                points = [];
                pointsByDisplay[id] = points;
            }

            foreach (var sample in segment.Samples)
            {
                if (sample.Pose is not { } pose)
                {
                    continue;
                }

                faceSampleCount++;
                if (features.PointOf(pose) is { } point)
                {
                    points.Add(point);
                }
            }
        }

        // Step 3: faces without the features is a different problem from too little data.
        if (!pointsByDisplay.Values.Any(p => p.Count > 0))
        {
            throw faceSampleCount > 0
                ? new CalibrationException(CalibrationFailure.FeaturesMissing, features)
                : new CalibrationException(CalibrationFailure.TooFewDisplays);
        }

        // Step 4: keep displays with enough points, in first-appearance order for now.
        var kept = firstAppearance.Where(id => pointsByDisplay[id].Count >= MinPointsPerDisplay).ToList();
        if (kept.Count < 2)
        {
            throw new CalibrationException(CalibrationFailure.TooFewDisplays);
        }

        // Steps 5 and 6: statistics per display, then the layout's left-to-right order.
        var profiles = OrderedDisplays(kept, trace.Layout)
            .Select(id => DisplayProfileOf(id, pointsByDisplay[id]))
            .ToList();

        // Step 7: pooled within-display spread, floored; pitch never weighs more than yaw.
        var yawScale = Math.Max(YawScaleFloor, RootMeanSquare(profiles.Select(p => p.YawStd)));
        var pitchScale = Math.Max(Math.Max(PitchScaleFloor, yawScale), RootMeanSquare(profiles.Select(p => p.PitchStd)));

        // Step 8: bookkeeping fields.
        return new CalibrationProfile(
            features, profiles, yawScale, pitchScale, trace.Layout?.Fingerprint ?? string.Empty, trace.CameraName, trace.CreatedAt);
    }

    /// <summary>
    /// The layout's displays that were kept, in layout order, followed by kept displays the layout does not contain,
    /// in order of first appearance.
    /// </summary>
    private static List<DisplayId> OrderedDisplays(List<DisplayId> kept, DisplayLayout? layout)
    {
        var keptSet = kept.ToHashSet();
        var seen = new HashSet<DisplayId>();
        var ordered = new List<DisplayId>();
        foreach (var display in layout?.Displays ?? [])
        {
            if (keptSet.Contains(display.Id) && seen.Add(display.Id))
            {
                ordered.Add(display.Id);
            }
        }

        foreach (var id in kept)
        {
            if (seen.Add(id))
            {
                ordered.Add(id);
            }
        }

        return ordered;
    }

    private static DisplayProfile DisplayProfileOf(DisplayId id, List<FeaturePoint> points)
    {
        var (yawMean, yawStd) = MeanAndStandardDeviation(points.Select(p => p.X).ToList());
        var (pitchMean, pitchStd) = MeanAndStandardDeviation(points.Select(p => p.Y).ToList());
        return new DisplayProfile(id, yawMean, pitchMean, yawStd, pitchStd, points.Count);
    }

    /// <summary>
    /// Mean and sample standard deviation (n - 1; 0 when there are fewer than two values). Two passes, so the result
    /// stays accurate for angles that sit far from zero.
    /// </summary>
    private static (double Mean, double Std) MeanAndStandardDeviation(List<double> values)
    {
        if (values.Count == 0)
        {
            return (0, 0);
        }

        var mean = values.Aggregate(0.0, (sum, v) => sum + v) / values.Count;
        if (values.Count < 2)
        {
            return (mean, 0);
        }

        var squares = values.Aggregate(0.0, (sum, v) => sum + ((v - mean) * (v - mean)));
        return (mean, Math.Sqrt(squares / (values.Count - 1)));
    }

    private static double RootMeanSquare(IEnumerable<double> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? 0 : Math.Sqrt(list.Aggregate(0.0, (sum, v) => sum + (v * v)) / list.Count);
    }
}
