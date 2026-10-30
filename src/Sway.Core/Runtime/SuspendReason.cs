namespace Sway.Core.Runtime;

/// <summary>Why capture was suspended by the system. Capture resumes automatically when the condition ends.</summary>
public enum SuspendReason
{
    /// <summary>The PC is going to sleep (power suspend).</summary>
    SystemSleep,

    /// <summary>The displays are off.</summary>
    DisplaysAsleep,

    /// <summary>The workstation is locked.</summary>
    ScreenLocked,

    /// <summary>Another user's session is active (fast user switching, remote console switch).</summary>
    SessionInactive,
}
