namespace Gander.Core;

/// <summary>Mean and spread of yaw and pitch for one label (one monitor).</summary>
public sealed record LabelStats(string Label, int Count, double MeanYaw, double StdYaw, double MeanPitch, double StdPitch);

/// <summary>How well two labels separate: the distance between their centers and Fisher's d-prime along the line joining them.</summary>
public sealed record PairSeparation(string First, string Second, double CenterDistanceDegrees, double YawDifferenceDegrees, double DPrime);

/// <summary>The gate verdict.</summary>
public enum SeparabilityVerdict
{
    /// <summary>Not enough labeled samples with a face to judge.</summary>
    InsufficientData,

    /// <summary>The monitors do not separate well enough.</summary>
    DoesNotSeparate,

    /// <summary>Usable but close to the edge.</summary>
    Marginal,

    /// <summary>The monitors separate clearly.</summary>
    SeparatesClearly,
}

/// <summary>Everything the pose gate reports.</summary>
public sealed record SeparabilityResult(
    IReadOnlyList<LabelStats> Labels,
    IReadOnlyList<PairSeparation> Pairs,
    double? HeldOutAccuracy,
    double FaceRate,
    SeparabilityVerdict Verdict,
    string VerdictText);

/// <summary>
/// Decides whether head pose tells the monitors apart. Port of the Mac gate maths: per-label statistics, pairwise
/// d-prime, held-out nearest-centroid accuracy, and a verdict. Only samples with a face and a label count.
/// </summary>
public static class SeparabilityAnalyzer
{
    /// <summary>Standard deviations are floored at this many degrees so a very still head cannot divide by nearly zero.</summary>
    public const double MinimumStd = 1.0;

    /// <summary>Fewest face samples a label needs before the verdict means anything.</summary>
    public const int MinimumSamplesPerLabel = 10;

    /// <summary>Held-out accuracy and d-prime needed for "separates clearly".</summary>
    public const double ClearAccuracy = 0.95;

    /// <summary>The d-prime every pair needs for "separates clearly".</summary>
    public const double ClearDPrime = 3.0;

    /// <summary>Held-out accuracy needed for "marginal".</summary>
    public const double MarginalAccuracy = 0.85;

    /// <summary>Analyzes the samples. Labels keep the order in which they first appear.</summary>
    public static SeparabilityResult Analyze(IReadOnlyList<PoseSample> samples)
    {
        var labeled = samples.Where(sample => sample.Label.Length > 0).ToList();
        var withFace = labeled.Where(sample => sample.Face).ToList();
        var faceRate = labeled.Count == 0 ? 0 : (double)withFace.Count / labeled.Count;

        var names = labeled.Select(sample => sample.Label).Distinct().ToList();
        var clouds = names.Select(name => withFace.Where(sample => sample.Label == name).ToList()).ToList();
        var stats = names.Select((name, index) => Statistics(name, clouds[index])).ToList();

        var pairs = new List<PairSeparation>();
        for (var first = 0; first < names.Count; first++)
        {
            for (var second = first + 1; second < names.Count; second++)
            {
                pairs.Add(Separate(names[first], names[second], clouds[first], clouds[second]));
            }
        }

        var enough = names.Count >= 2 && clouds.All(cloud => cloud.Count >= MinimumSamplesPerLabel);
        var accuracy = enough ? HeldOutAccuracy(names, withFace) : null;
        var verdict = enough ? Decide(pairs, accuracy) : SeparabilityVerdict.InsufficientData;
        return new SeparabilityResult(stats, pairs, accuracy, faceRate, verdict, Describe(verdict));
    }

    /// <summary>The words used for a verdict in the report.</summary>
    public static string Describe(SeparabilityVerdict verdict) => verdict switch
    {
        SeparabilityVerdict.SeparatesClearly => "separates clearly",
        SeparabilityVerdict.Marginal => "marginal",
        SeparabilityVerdict.DoesNotSeparate => "does not separate",
        _ => "not enough data",
    };

    private static SeparabilityVerdict Decide(List<PairSeparation> pairs, double? accuracy)
    {
        if (accuracy is not { } held)
        {
            return SeparabilityVerdict.InsufficientData;
        }

        if (held >= ClearAccuracy && pairs.All(pair => pair.DPrime >= ClearDPrime))
        {
            return SeparabilityVerdict.SeparatesClearly;
        }

        return held >= MarginalAccuracy ? SeparabilityVerdict.Marginal : SeparabilityVerdict.DoesNotSeparate;
    }

    private static LabelStats Statistics(string label, List<PoseSample> cloud)
    {
        var (meanYaw, meanPitch, varYaw, _, varPitch) = Moments(cloud);
        return new LabelStats(label, cloud.Count, meanYaw, Math.Sqrt(varYaw), meanPitch, Math.Sqrt(varPitch));
    }

    /// <summary>Mean, and sample (n - 1) variances and covariance, of yaw (x) and pitch (y).</summary>
    private static (double MeanX, double MeanY, double VarXX, double VarXY, double VarYY) Moments(List<PoseSample> cloud)
    {
        if (cloud.Count == 0)
        {
            return (0, 0, 0, 0, 0);
        }

        var meanX = cloud.Average(sample => sample.Yaw);
        var meanY = cloud.Average(sample => sample.Pitch);
        if (cloud.Count == 1)
        {
            return (meanX, meanY, 0, 0, 0);
        }

        double xx = 0, xy = 0, yy = 0;
        foreach (var sample in cloud)
        {
            var dx = sample.Yaw - meanX;
            var dy = sample.Pitch - meanY;
            xx += dx * dx;
            xy += dx * dy;
            yy += dy * dy;
        }

        var n = cloud.Count - 1;
        return (meanX, meanY, xx / n, xy / n, yy / n);
    }

    /// <summary>
    /// Distance between the means and d-prime along the line joining them: each class variance along that direction
    /// is floored at <see cref="MinimumStd"/> squared, and d' = distance / sqrt(mean of the two variances).
    /// </summary>
    private static PairSeparation Separate(string first, string second, List<PoseSample> a, List<PoseSample> b)
    {
        var ma = Moments(a);
        var mb = Moments(b);
        var dx = ma.MeanX - mb.MeanX;
        var dy = ma.MeanY - mb.MeanY;
        var distance = Math.Sqrt((dx * dx) + (dy * dy));
        if (distance <= 0)
        {
            return new PairSeparation(first, second, 0, 0, 0);
        }

        var ux = dx / distance;
        var uy = dy / distance;
        double Along((double MeanX, double MeanY, double VarXX, double VarXY, double VarYY) m) =>
            Math.Max((ux * ux * m.VarXX) + (2 * ux * uy * m.VarXY) + (uy * uy * m.VarYY), MinimumStd * MinimumStd);

        var dPrime = distance / Math.Sqrt((Along(ma) + Along(mb)) / 2);
        return new PairSeparation(first, second, distance, Math.Abs(dx), dPrime);
    }

    /// <summary>
    /// Nearest-centroid accuracy on data the centroids never saw. With two or more rounds it is leave-one-round-out
    /// (train on the other rounds, test on this one; with two rounds that is round 1 to 2 and round 2 to 1). With one
    /// round, each label's first half trains and its second half tests. Null when there is nothing to test.
    /// </summary>
    private static double? HeldOutAccuracy(List<string> names, List<PoseSample> withFace)
    {
        var rounds = withFace.Select(sample => sample.Round).Distinct().OrderBy(round => round).ToList();
        var correct = 0;
        var total = 0;

        void Score(List<List<PoseSample>> train, List<List<PoseSample>> test)
        {
            var centroids = train.Select(Centroid).ToList();
            for (var label = 0; label < names.Count; label++)
            {
                foreach (var sample in test[label])
                {
                    total++;
                    if (Nearest(centroids, sample) == label)
                    {
                        correct++;
                    }
                }
            }
        }

        if (rounds.Count >= 2)
        {
            foreach (var held in rounds)
            {
                Score(
                    names.Select(name => withFace.Where(s => s.Label == name && s.Round != held).ToList()).ToList(),
                    names.Select(name => withFace.Where(s => s.Label == name && s.Round == held).ToList()).ToList());
            }
        }
        else
        {
            var perLabel = names.Select(name => withFace.Where(s => s.Label == name).OrderBy(s => s.TimeSeconds).ToList()).ToList();
            Score(
                perLabel.Select(list => list.Take(list.Count / 2).ToList()).ToList(),
                perLabel.Select(list => list.Skip(list.Count / 2).ToList()).ToList());
        }

        return total > 0 ? (double)correct / total : null;
    }

    /// <summary>A candidate cluster; null when it has no training samples (then it cannot win).</summary>
    private static (double MeanX, double MeanY, double StdX, double StdY)? Centroid(List<PoseSample> cloud)
    {
        if (cloud.Count == 0)
        {
            return null;
        }

        var m = Moments(cloud);
        return (m.MeanX, m.MeanY, Math.Sqrt(m.VarXX), Math.Sqrt(m.VarYY));
    }

    /// <summary>Index of the nearest centroid by diagonal z-distance (std floored at <see cref="MinimumStd"/>); ties go to the earlier one.</summary>
    private static int Nearest(List<(double MeanX, double MeanY, double StdX, double StdY)?> centroids, PoseSample sample)
    {
        var best = -1;
        var bestDistance = double.MaxValue;
        for (var index = 0; index < centroids.Count; index++)
        {
            if (centroids[index] is not { } c)
            {
                continue;
            }

            var zx = (sample.Yaw - c.MeanX) / Math.Max(c.StdX, MinimumStd);
            var zy = (sample.Pitch - c.MeanY) / Math.Max(c.StdY, MinimumStd);
            var distance = Math.Sqrt((zx * zx) + (zy * zy));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = index;
            }
        }

        return best;
    }
}
