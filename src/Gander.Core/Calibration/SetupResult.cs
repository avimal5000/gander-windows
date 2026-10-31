namespace Gander.Core.Calibration;

/// <summary>How well a new calibration separates the displays (Mac <c>SetupResult.Quality</c>).</summary>
public enum CalibrationQuality
{
    /// <summary>"Calibration looks good."</summary>
    Good,

    /// <summary>Two displays are close together, or the face was seen only briefly somewhere.</summary>
    Fair,

    /// <summary>Two displays are hard to tell apart.</summary>
    Poor,

    /// <summary>The fit failed and nothing was saved.</summary>
    Failed,
}

/// <summary>
/// What the Setup window's last step says (SPEC F20, CONTRACT 6.5.3): how well the new calibration separates the
/// displays, or why nothing was saved. Port of the Mac <c>SetupResult</c>.
/// </summary>
public sealed record SetupResult(CalibrationQuality Quality, string Text)
{
    /// <summary>The words for a calibration that looks good.</summary>
    public const string GoodText = "Calibration looks good.";

    /// <summary>True when a calibration was saved.</summary>
    public bool IsSaved => Quality != CalibrationQuality.Failed;

    /// <summary>The fit failed: nothing was saved. <paramref name="text"/> is the reason.</summary>
    public static SetupResult Failed(string text) => new(CalibrationQuality.Failed, text);

    /// <summary>
    /// The fit left out the displays named in <paramref name="names"/> (too few face samples while looking at them):
    /// nothing was saved.
    /// </summary>
    public static SetupResult MissingDisplays(IReadOnlyList<string> names) =>
        Failed(
            $"Gander couldn't see your face well enough while you looked at {Listed(names)}. "
            + "Make sure the camera sees you from every monitor, then calibrate again.");

    /// <summary>"A", "A and B", "A, B, and C".</summary>
    public static string Listed(IReadOnlyList<string> names) => names.Count switch
    {
        0 => string.Empty,
        1 => names[0],
        2 => $"{names[0]} and {names[1]}",
        _ => string.Join(", ", names.Take(names.Count - 1)) + ", and " + names[^1],
    };

    /// <summary>
    /// The verdict of <paramref name="analysis"/> in words. With no pair to name (no pairs in the analysis) it reads
    /// without naming one.
    /// </summary>
    /// <param name="analysis">The separability result of the calibration trace, or null for none (reads as good).</param>
    /// <param name="name">The label of a display, by the raw id the analysis uses as label.</param>
    public static SetupResult FromAnalysis(SeparabilityResult? analysis, Func<string, string> name)
    {
        var weakest = analysis?.Pairs.Count > 0 ? analysis.Pairs.MinBy(p => p.DPrime) : null;
        (string First, string Second)? pair = weakest is null ? null : (name(weakest.First), name(weakest.Second));
        switch (analysis?.Verdict)
        {
            case SeparabilityVerdict.Marginal:
                return new SetupResult(
                    CalibrationQuality.Fair,
                    pair is { } m
                        ? $"Calibration saved. {m.First} and {m.Second} are close together, so switching between them may be slower."
                        : "Calibration saved. Some monitors are close together, so switching between them may be slower.");
            case SeparabilityVerdict.DoesNotSeparate:
                return new SetupResult(
                    CalibrationQuality.Poor,
                    pair is { } p
                        ? $"Calibration saved, but {p.First} and {p.Second} are hard to tell apart. Try moving the camera, then calibrate again."
                        : "Calibration saved, but some monitors are hard to tell apart. Try moving the camera, then calibrate again.");
            case SeparabilityVerdict.InsufficientData:
                // Setup saves only a calibration that covers every display, so this means few samples somewhere.
                return new SetupResult(
                    CalibrationQuality.Fair,
                    "Calibration saved, but Gander saw your face only briefly on some monitors. If switching feels unreliable, calibrate again.");
            default:
                return new SetupResult(CalibrationQuality.Good, GoodText);
        }
    }
}
