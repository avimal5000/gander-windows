using System;
using Gander.App.Presentation.Design;
using Gander.Core.Platform;

namespace Gander.App.Presentation;

/// <summary>
/// The settings that belong to the app shell rather than the controller: the accent, the edge glow and the Focus Lock
/// notice (Mac Settings > Appearance). Saved under the Mac's key names. The glow and the notice are on when absent.
/// </summary>
public sealed class AppPreferences
{
    /// <summary>"true" or "false": the edge glow on the display that gets focus.</summary>
    public const string EdgeGlowKey = "gander.edgeGlowEnabled";

    /// <summary>"true" or "false": a notice when the shortcut toggles Focus Lock.</summary>
    public const string FocusLockNoticeKey = "gander.focusLockNoticeEnabled";

    /// <summary>"true" once the onboarding walkthrough has been shown; absent (or anything else) means not yet.</summary>
    public const string OnboardingSeenKey = "gander.onboardingSeen";

    /// <summary>"true" once the walkthrough reached the camera check; absent (or anything else) means not yet.</summary>
    public const string SetupCompletedKey = "gander.setupCompleted";

    private readonly ISettingsStore _store;

    /// <summary>Reads the saved values.</summary>
    public AppPreferences(ISettingsStore store)
    {
        _store = store;
        EdgeGlowEnabled = ParseFlag(Read(EdgeGlowKey));
        FocusLockNoticeEnabled = ParseFlag(Read(FocusLockNoticeKey));
        Accent = AccentChoices.Parse(Read(AccentChoices.Key));
        OnboardingSeen = ParseSeen(Read(OnboardingSeenKey));
        SetupCompleted = ParseSeen(Read(SetupCompletedKey));
    }

    /// <summary>Raised after any value changed.</summary>
    public event Action? Changed;

    /// <summary>The glow is on.</summary>
    public bool EdgeGlowEnabled { get; private set; }

    /// <summary>The notice is on.</summary>
    public bool FocusLockNoticeEnabled { get; private set; }

    /// <summary>The accent choice.</summary>
    public AccentChoice Accent { get; private set; }

    /// <summary>The walkthrough has been shown once. False for a settings file that never had the field.</summary>
    public bool OnboardingSeen { get; private set; }

    /// <summary>The first setup is done: the menu stops offering it and the intro does not replay by itself.</summary>
    public bool SetupCompleted { get; private set; }

    /// <summary>The saved "seen" flag; false unless it is exactly "true".</summary>
    public static bool ParseSeen(string? stored) => string.Equals(stored, "true", StringComparison.Ordinal);

    /// <summary>A saved on/off setting; on when absent or unreadable.</summary>
    public static bool ParseFlag(string? stored) => !string.Equals(stored, "false", StringComparison.Ordinal);

    /// <summary>Turns the glow on or off and saves it.</summary>
    public void SetEdgeGlow(bool enabled)
    {
        if (enabled == EdgeGlowEnabled)
        {
            return;
        }

        EdgeGlowEnabled = enabled;
        Save(EdgeGlowKey, enabled ? "true" : "false");
    }

    /// <summary>Turns the Focus Lock notice on or off and saves it.</summary>
    public void SetFocusLockNotice(bool enabled)
    {
        if (enabled == FocusLockNoticeEnabled)
        {
            return;
        }

        FocusLockNoticeEnabled = enabled;
        Save(FocusLockNoticeKey, enabled ? "true" : "false");
    }

    /// <summary>Chooses the accent and saves it.</summary>
    public void SetAccent(AccentChoice choice)
    {
        if (choice == Accent)
        {
            return;
        }

        Accent = choice;
        Save(AccentChoices.Key, AccentChoices.Store(choice));
    }

    /// <summary>Records that the walkthrough was shown. Saved once; it raises no <see cref="Changed"/> (no appearance changes).</summary>
    public void MarkOnboardingSeen()
    {
        if (OnboardingSeen)
        {
            return;
        }

        OnboardingSeen = true;
        try
        {
            _store.Write(OnboardingSeenKey, "true");
        }
        catch (Exception)
        {
            // Stays in memory for this run; the window will not reopen before the next start.
        }
    }

    /// <summary>Records that the first setup is done. Saved once; it raises no <see cref="Changed"/>.</summary>
    public void MarkSetupCompleted()
    {
        if (SetupCompleted)
        {
            return;
        }

        SetupCompleted = true;
        try
        {
            _store.Write(SetupCompletedKey, "true");
        }
        catch (Exception)
        {
            // Stays in memory for this run.
        }
    }

    private string? Read(string key)
    {
        try
        {
            return _store.Read(key);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void Save(string key, string value)
    {
        try
        {
            _store.Write(key, value);
        }
        catch (Exception)
        {
            // The value stays in memory for this run.
        }

        Changed?.Invoke();
    }
}
