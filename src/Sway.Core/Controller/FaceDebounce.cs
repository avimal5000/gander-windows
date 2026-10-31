namespace Sway.Core.Controller;

/// <summary>
/// Steadies the "No face" status (Mac CONTRACT 6.5.5 item 8): a face shows at once, but "no face" only after the face has
/// been missing for <see cref="AbsentDelay"/>. Pure and clock-free: it works on the samples' own timestamps.
/// </summary>
public sealed class FaceDebounce
{
    /// <summary>How long a face must be missing before <see cref="IsPresent"/> turns false, in seconds.</summary>
    public const double AbsentDelay = 0.5;

    private double? _lastSeen;

    /// <summary>What the status should treat as "a face is there".</summary>
    public bool IsPresent { get; private set; }

    /// <summary>Feeds one sample and returns <see cref="IsPresent"/>.</summary>
    public bool Update(bool face, double timestamp)
    {
        if (face)
        {
            _lastSeen = timestamp;
            IsPresent = true;
        }
        else if (IsPresent && _lastSeen is { } seen)
        {
            // A clock that restarted lower must not pin the status to "face" until it catches up.
            var reference = Math.Min(seen, timestamp);
            _lastSeen = reference;
            if (timestamp - reference >= AbsentDelay)
            {
                IsPresent = false;
            }
        }

        return IsPresent;
    }

    /// <summary>Forgets everything: the camera stopped, restarted, or was interrupted.</summary>
    public void Reset()
    {
        IsPresent = false;
        _lastSeen = null;
    }
}
