using Sway.App.Presentation;
using Sway.Core;
using Sway.Core.Platform;

namespace Sway.Platform.Windows.Tests.App;

public sealed class FocusTestModelTests
{
    private static DisplayInfo Display(string id, string name, double x) =>
        new(id, 1, name, new DisplayRect(x, 0, 1920, 1080), false, x == 0);

    private static WindowSummary Window(string app, string display) =>
        new(1, 10, app, new DisplayRect(0, 0, 100, 100), display);

    private static readonly DisplayLayout TwoSame = new([Display("a", "Monitor", 0), Display("b", "Monitor", 1920)]);

    [Fact]
    public void RowsFollowLayoutOrderWithUniqueLabelsAndShowAppNamesOnly()
    {
        var windows = new Dictionary<DisplayId, WindowSummary> { ["a"] = Window("Notepad", "a") };
        var rows = FocusTestWording.Rows(TwoSame, windows, "b");
        Assert.Equal(2, rows.Count);
        Assert.Equal("Monitor (1)", rows[0].Title);
        Assert.Equal("Notepad", rows[0].WindowText);
        Assert.True(rows[0].HasWindow);
        Assert.Equal("Monitor (2) (focused)", rows[1].Title);
        Assert.True(rows[1].IsFocused);
        Assert.Equal("No window", rows[1].WindowText);
        Assert.False(rows[1].HasWindow);
    }

    [Fact]
    public void NothingFocusedAndNoDisplays()
    {
        Assert.All(FocusTestWording.Rows(TwoSame, new Dictionary<DisplayId, WindowSummary>(), null), r => Assert.False(r.IsFocused));
        Assert.Empty(FocusTestWording.Rows(DisplayLayout.Empty, new Dictionary<DisplayId, WindowSummary>(), null));
    }

    [Fact]
    public void LogLinesNameTheAppAndDisplayNeverATitle()
    {
        string Name(DisplayId id) => id.RawValue == "a" ? "Left" : "Right";
        Assert.Equal(
            "Focused Notepad on Left via attached input in 42 ms",
            FocusTestWording.LogText(new FocusResult.Focused(Window("Notepad", "a"), FocusMethod.AttachInput, 42), Name));
        Assert.Equal(
            "Focused Code on Right via plain call in 3 ms",
            FocusTestWording.LogText(new FocusResult.Focused(Window("Code", "b"), FocusMethod.Plain, 3), Name));
        Assert.Equal("No window on Right", FocusTestWording.LogText(new FocusResult.NoWindow("b"), Name));
        Assert.Equal("Failed: it would not raise", FocusTestWording.LogText(new FocusResult.Failed(null, "it would not raise"), Name));
    }

    [Fact]
    public void WordingMatchesTheMacAndPromisesTheCursorNeverMoves()
    {
        Assert.Equal("Focus", FocusTestWording.FocusButton);
        Assert.Equal("Focus in 3 s", FocusTestWording.DelayedButton);
        Assert.Equal("Cancel (2 s)", FocusTestWording.CancelButton(2));
        Assert.Equal("Displays (left to right)", FocusTestWording.DisplaysHeading);
        Assert.Equal("Results appear here.", FocusTestWording.NoResults);
        Assert.Contains("The cursor never moves.", FocusTestWording.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void CountdownTicksDownAndFiresOnceAtThreeSeconds()
    {
        var state = new FocusTestState();
        Assert.True(state.CanRequest);
        Assert.True(state.BeginCountdown("a", 10));
        Assert.Equal(FocusActivityKind.CountingDown, state.Activity);
        Assert.False(state.CanRequest);
        Assert.Equal(3, state.CountdownSeconds);
        Assert.Null(state.Tick(10.5));
        Assert.Equal(3, state.CountdownSeconds);
        Assert.Null(state.Tick(11.5));
        Assert.Equal(2, state.CountdownSeconds);
        Assert.Null(state.Tick(12.9));
        Assert.Equal(1, state.CountdownSeconds);
        Assert.Equal(new DisplayId("a"), state.Tick(13));
        Assert.True(state.CanRequest);
        Assert.Null(state.Tick(14));
    }

    [Fact]
    public void OneRequestAtATimeAndACountdownCanBeCancelled()
    {
        var state = new FocusTestState();
        Assert.True(state.BeginCountdown("a", 0));
        Assert.False(state.BeginCountdown("b", 0));
        Assert.False(state.BeginFocus("b"));
        state.CancelCountdown();
        Assert.True(state.CanRequest);
        Assert.Null(state.Tick(100));

        Assert.True(state.BeginFocus("b"));
        Assert.Equal(FocusActivityKind.Focusing, state.Activity);
        Assert.False(state.BeginCountdown("a", 0));
        state.CancelCountdown(); // not counting: does nothing
        Assert.Equal(FocusActivityKind.Focusing, state.Activity);
        state.Finish("done", DateTimeOffset.UnixEpoch);
        Assert.True(state.CanRequest);
    }

    [Fact]
    public void LogKeepsTheLastTenNewestFirst()
    {
        var state = new FocusTestState();
        for (var i = 0; i < 14; i++)
        {
            state.BeginFocus("a");
            state.Finish($"result {i}", DateTimeOffset.UnixEpoch.AddSeconds(i));
        }

        Assert.Equal(FocusTestState.LogLimit, state.Log.Count);
        Assert.Equal("result 13", state.Log[0].Text);
        Assert.Equal("result 4", state.Log[^1].Text);
        Assert.Equal(state.Log.Count, state.Log.Select(e => e.Id).Distinct().Count());
        state.Reset();
        Assert.Equal(FocusTestState.LogLimit, state.Log.Count);
    }
}
