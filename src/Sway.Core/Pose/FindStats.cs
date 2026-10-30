namespace Sway.Core.Pose;

/// <summary>How a frame's face was found.</summary>
public enum FaceSource
{
    /// <summary>No face this frame.</summary>
    NotFound,

    /// <summary>The tracked box from the previous frame worked.</summary>
    Tracked,

    /// <summary>The system face detector supplied the box.</summary>
    Detector,

    /// <summary>A search-plan candidate box supplied the face (the detector found nothing).</summary>
    Fallback,
}

/// <summary>Counts how frames were found and the longest run of consecutive lost frames.</summary>
public sealed class FindStats
{
    private int _currentLost;

    /// <summary>Frames recorded.</summary>
    public int Frames { get; private set; }

    /// <summary>Frames found by the tracked box.</summary>
    public int Tracked { get; private set; }

    /// <summary>Frames found through the system detector.</summary>
    public int Detector { get; private set; }

    /// <summary>Frames found through a fallback candidate box.</summary>
    public int Fallback { get; private set; }

    /// <summary>Frames with no face.</summary>
    public int NotFound { get; private set; }

    /// <summary>The longest run of consecutive frames with no face.</summary>
    public int LongestLostRun { get; private set; }

    /// <summary>Records one frame.</summary>
    public void Add(FaceSource source)
    {
        Frames++;
        switch (source)
        {
            case FaceSource.Tracked:
                Tracked++;
                break;
            case FaceSource.Detector:
                Detector++;
                break;
            case FaceSource.Fallback:
                Fallback++;
                break;
            default:
                NotFound++;
                break;
        }

        if (source == FaceSource.NotFound)
        {
            _currentLost++;
            LongestLostRun = Math.Max(LongestLostRun, _currentLost);
        }
        else
        {
            _currentLost = 0;
        }
    }

    /// <summary>One-line summary for the live readout and the report.</summary>
    public string Summary() =>
        $"found by tracking {Tracked}, detector {Detector}, fallback boxes {Fallback}; not found {NotFound}; longest lost run {LongestLostRun} frames";
}
