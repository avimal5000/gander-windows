using System.Text.Json;

namespace Gander.Core.Tests;

public class PoseTraceTests
{
    [Fact]
    public void RoundTripsThroughJson()
    {
        var trace = new PoseTrace(new[]
        {
            new PoseSample(0.0, 12.5, -3.25, 1.0, "monitor 1", 1, true),
            new PoseSample(0.033, 0, 0, 0, "monitor 2", 2, false),
        });

        var back = PoseTrace.FromJson(trace.ToJson());

        Assert.Equal(trace.Samples, back.Samples);
    }

    [Fact]
    public void JsonHoldsOnlyNumbersAndLabels()
    {
        var json = new PoseTrace(new[] { new PoseSample(1, 2, 3, 4, "monitor 1", 1) }).ToJson();
        using var document = JsonDocument.Parse(json);
        var names = document.RootElement[0].EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(new[] { "timeSeconds", "yaw", "pitch", "roll", "label", "round", "face" }, names);
    }

    [Fact]
    public void BadJsonThrows()
    {
        Assert.ThrowsAny<JsonException>(() => PoseTrace.FromJson("not json"));
        Assert.Throws<JsonException>(() => PoseTrace.FromJson("null"));
    }
}
