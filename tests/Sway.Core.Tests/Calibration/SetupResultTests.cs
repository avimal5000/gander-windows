using Sway.Core.Calibration;

namespace Sway.Core.Tests.Calibration;

/// <summary>Ports of the verdict and wording tests of the Mac CalibrationRunTests.</summary>
public sealed class SetupResultTests
{
    private static string Name(string id) => id switch
    {
        "display-left" => "Left",
        "display-right" => "Right",
        "display-far" => "Far",
        _ => "?",
    };

    private static SeparabilityResult Analysis(SeparabilityVerdict verdict, params (string, string, double)[] pairs) =>
        new([], pairs.Select(p => new PairSeparation(p.Item1, p.Item2, 10, 10, p.Item3)).ToList(), null, 1, verdict, SeparabilityAnalyzer.Describe(verdict));

    private static readonly (string, string, double)[] Three =
    [
        ("display-left", "display-right", 9), ("display-left", "display-far", 14), ("display-right", "display-far", 2.5),
    ];

    [Fact]
    public void TheWeakestPairIsTheOneNamed()
    {
        var marginal = SetupResult.FromAnalysis(Analysis(SeparabilityVerdict.Marginal, Three), Name);
        Assert.Equal("Calibration saved. Right and Far are close together, so switching between them may be slower.", marginal.Text);
        Assert.Equal(CalibrationQuality.Fair, marginal.Quality);
        Assert.True(marginal.IsSaved);

        var poor = SetupResult.FromAnalysis(Analysis(SeparabilityVerdict.DoesNotSeparate, Three), Name);
        Assert.Equal(
            "Calibration saved, but Right and Far are hard to tell apart. Try moving the camera, then calibrate again.", poor.Text);
        Assert.Equal(CalibrationQuality.Poor, poor.Quality);
        Assert.True(poor.IsSaved);
    }

    [Fact]
    public void AVerdictWithoutAPairStillReads()
    {
        var marginal = SetupResult.FromAnalysis(Analysis(SeparabilityVerdict.Marginal), Name);
        var poor = SetupResult.FromAnalysis(Analysis(SeparabilityVerdict.DoesNotSeparate), Name);
        Assert.Equal(CalibrationQuality.Fair, marginal.Quality);
        Assert.StartsWith("Calibration saved.", marginal.Text);
        Assert.Equal(CalibrationQuality.Poor, poor.Quality);
        Assert.EndsWith("Try moving the camera, then calibrate again.", poor.Text);
    }

    [Fact]
    public void ClearSeparationAndNoAnalysisReadAsGood()
    {
        Assert.Equal(CalibrationQuality.Good, SetupResult.FromAnalysis(Analysis(SeparabilityVerdict.SeparatesClearly, Three), Name).Quality);
        var none = SetupResult.FromAnalysis(null, Name);
        Assert.Equal(CalibrationQuality.Good, none.Quality);
        Assert.Equal("Calibration looks good.", none.Text);
    }

    [Fact]
    public void InsufficientDataSuggestsCalibratingAgain()
    {
        var result = SetupResult.FromAnalysis(Analysis(SeparabilityVerdict.InsufficientData), Name);
        Assert.Equal(CalibrationQuality.Fair, result.Quality);
        Assert.Contains("calibrate again", result.Text);
        Assert.True(result.IsSaved);
    }

    [Fact]
    public void AFailedResultIsNotSaved()
    {
        var failed = SetupResult.Failed("Because.");
        Assert.Equal(CalibrationQuality.Failed, failed.Quality);
        Assert.Equal("Because.", failed.Text);
        Assert.False(failed.IsSaved);
        Assert.Equal("Calibration looks good.", SetupResult.GoodText);
    }

    [Fact]
    public void MissingDisplaysAreListedInPlainEnglish()
    {
        Assert.Equal("A", SetupResult.Listed(["A"]));
        Assert.Equal("A and B", SetupResult.Listed(["A", "B"]));
        Assert.Equal("A, B, and C", SetupResult.Listed(["A", "B", "C"]));
        var result = SetupResult.MissingDisplays(["Left", "Far"]);
        Assert.False(result.IsSaved);
        Assert.StartsWith("Gander couldn't see your face well enough while you looked at Left and Far.", result.Text);
    }

    [Fact]
    public void TheSetupTextsMatchTheSpec()
    {
        Assert.Equal(
            "Put the camera where it sees your face from every monitor. On top of the middle monitor works best.",
            SetupPresentation.CameraAdvice);
        Assert.Equal("Connect a second monitor to calibrate.", SetupPresentation.NeedsSecondDisplay);
        Assert.Equal("Calibration cancelled.", SetupPresentation.CalibrationCancelled);
        Assert.Contains("Privacy & security > Camera", SetupPresentation.CameraDeniedMessage);
        Assert.Equal("Calibrate again.", SetupPresentation.TryAgain);
    }
}
