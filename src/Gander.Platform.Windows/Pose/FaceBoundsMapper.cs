using Gander.Core;

namespace Gander.Platform.Windows.Pose;

/// <summary>Turns the landmark box (frame pixels) into the normalized face rectangle the framing advice reads. Numbers only.</summary>
internal static class FaceBoundsMapper
{
    /// <summary>
    /// The smallest rectangle around the landmarks as fractions of the frame, clamped to the frame. Null for no
    /// landmarks, a non-finite point, an empty frame, or a box with no area.
    /// </summary>
    internal static FaceBounds? FromLandmarks(IReadOnlyList<Landmark> landmarks, int frameWidth, int frameHeight)
    {
        if (landmarks.Count == 0 || frameWidth <= 0 || frameHeight <= 0)
        {
            return null;
        }

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var point in landmarks)
        {
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            {
                return null;
            }

            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        var left = Math.Clamp(minX / frameWidth, 0, 1);
        var top = Math.Clamp(minY / frameHeight, 0, 1);
        var right = Math.Clamp(maxX / frameWidth, 0, 1);
        var bottom = Math.Clamp(maxY / frameHeight, 0, 1);
        return right > left && bottom > top ? new FaceBounds(left, top, right - left, bottom - top) : null;
    }
}
