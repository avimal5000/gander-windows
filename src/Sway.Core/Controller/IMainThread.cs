namespace Sway.Core.Controller;

/// <summary>
/// The thread that owns the tracking controller (the WPF dispatcher in the app). Events that arrive on any thread
/// (platform callbacks, the pose stream) are handed to it with <see cref="Post"/>; the controller never touches its
/// state from anywhere else. <see cref="Post"/> must queue, never run the action inline.
/// </summary>
public interface IMainThread
{
    /// <summary>Runs <paramref name="action"/> later on the main thread, in the order posted.</summary>
    void Post(Action action);
}
