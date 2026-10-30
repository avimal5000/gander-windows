namespace Sway.Core;

/// <summary>
/// Calibration fitting, pose classification, and the switch state machine. A stateless facade over
/// <see cref="ProfileFitter"/>, <see cref="PoseClassifier"/>, and <see cref="SwitchStateMachine"/>. Safe to share and
/// to call from any thread.
/// </summary>
public sealed class PoseSwitchEngine : ISwitchEngineMaking
{
    /// <inheritdoc />
    public CalibrationProfile FitProfile(LabeledPoseTrace trace, PoseFeatureSet features, IReadOnlySet<int>? rounds) =>
        new ProfileFitter().Fit(trace, features, rounds);

    /// <inheritdoc />
    public PoseClassification Classify(HeadPose? pose, CalibrationProfile profile, SwitchSettings settings) =>
        new PoseClassifier(profile, settings).Classify(pose);

    /// <inheritdoc />
    public ISwitchDecider MakeDecider(CalibrationProfile profile, SwitchSettings settings) =>
        new SwitchStateMachine(profile, settings);
}
