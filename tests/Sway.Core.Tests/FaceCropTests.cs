namespace Sway.Core.Tests;

public class FaceCropTests
{
    /// <summary>A frame where pixel (x, y) has blue = x, green = y, red = 100.</summary>
    private static byte[] Gradient(int width, int height)
    {
        var bytes = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var at = ((y * width) + x) * 4;
                bytes[at] = (byte)x;
                bytes[at + 1] = (byte)y;
                bytes[at + 2] = 100;
                bytes[at + 3] = 255;
            }
        }

        return bytes;
    }

    [Fact]
    public void IdentityCropCopiesPixelsAsRgbInNhwcOrder()
    {
        var frame = Gradient(8, 8);
        var tensor = new float[8 * 8 * 3];
        FaceCrop.FillTensor(frame, 8, 8, new SquareBox(4, 4, 8), 8, false, tensor);
        var at = ((3 * 8) + 5) * 3; // x = 5, y = 3
        Assert.Equal(100 / 255f, tensor[at], 4);
        Assert.Equal(3 / 255f, tensor[at + 1], 4);
        Assert.Equal(5 / 255f, tensor[at + 2], 4);
    }

    [Fact]
    public void ChannelsFirstPutsEachChannelInItsOwnPlane()
    {
        var frame = Gradient(8, 8);
        var tensor = new float[8 * 8 * 3];
        FaceCrop.FillTensor(frame, 8, 8, new SquareBox(4, 4, 8), 8, true, tensor);
        var at = (3 * 8) + 5;
        Assert.Equal(100 / 255f, tensor[at], 4);
        Assert.Equal(3 / 255f, tensor[64 + at], 4);
        Assert.Equal(5 / 255f, tensor[128 + at], 4);
    }

    [Fact]
    public void DownscalingInterpolatesBetweenPixels()
    {
        var frame = Gradient(8, 8);
        var tensor = new float[4 * 4 * 3];
        FaceCrop.FillTensor(frame, 8, 8, new SquareBox(4, 4, 8), 4, false, tensor);
        // Output pixel 0 samples source x = 0.5 (between blue 0 and 1): 0.5 / 255.
        Assert.Equal(0.5f / 255f, tensor[2], 4);
        // Output pixel 3 samples source x = 6.5.
        Assert.Equal(6.5f / 255f, tensor[(3 * 3) + 2], 4);
    }

    [Fact]
    public void AreaOutsideTheFrameIsBlack()
    {
        var frame = Gradient(8, 8);
        var tensor = new float[8 * 8 * 3];
        FaceCrop.FillTensor(frame, 8, 8, new SquareBox(-20, -20, 8), 8, false, tensor);
        Assert.All(tensor, value => Assert.Equal(0f, value));
    }

    [Fact]
    public void RejectsBuffersThatAreTooSmall()
    {
        Assert.Throws<ArgumentException>(() => FaceCrop.FillTensor(new byte[10], 8, 8, new SquareBox(4, 4, 8), 8, false, new float[192]));
        Assert.Throws<ArgumentException>(() => FaceCrop.FillTensor(Gradient(8, 8), 8, 8, new SquareBox(4, 4, 8), 8, false, new float[10]));
    }

    [Fact]
    public void MapsLandmarksBackToFramePixels()
    {
        var box = new SquareBox(300, 200, 128); // left 236, top 136; model size 256 so scale 0.5
        var mapped = FaceCrop.MapToFrame(new float[] { 0, 0, 10, 256, 256, -20, 128, 64, 0 }, 256, box);
        Assert.Equal(new Landmark(236, 136, 5), mapped[0]);
        Assert.Equal(new Landmark(364, 264, -10), mapped[1]);
        Assert.Equal(new Landmark(300, 168, 0), mapped[2]);
    }

    [Fact]
    public void TrackingBoxIsTheSquaredEnlargedLandmarkBounds()
    {
        var box = SquareBox.FromLandmarks(new[] { new Landmark(100, 50, 0), new Landmark(200, 250, 0) }, 1.5);
        Assert.NotNull(box);
        Assert.Equal(150, box.Value.CenterX, 6);
        Assert.Equal(150, box.Value.CenterY, 6);
        Assert.Equal(300, box.Value.Side, 6);
    }

    [Fact]
    public void NoTrackingBoxFromEmptyOrNonFiniteLandmarks()
    {
        Assert.Null(SquareBox.FromLandmarks(Array.Empty<Landmark>(), 1.5));
        Assert.Null(SquareBox.FromLandmarks(new[] { new Landmark(double.NaN, 0, 0) }, 1.5));
        Assert.Null(SquareBox.FromLandmarks(new[] { new Landmark(5, 5, 0) }, 1.5));
    }

    [Fact]
    public void SigmoidMapsLogitsToProbabilities()
    {
        Assert.Equal(0.5, FaceCrop.Sigmoid(0), 9);
        Assert.True(FaceCrop.Sigmoid(10) > 0.999);
        Assert.True(FaceCrop.Sigmoid(-10.5) < 0.001);
    }

    [Fact]
    public void ReadsTheChosenModelLayout()
    {
        var layout = ModelLayout.Parse(new[] { -1, 256, 256, 3 }, new[] { new[] { -1, 1, 1, 1434 }, new[] { -1, 1, 1, 1 }, new[] { -1, 1 } });
        Assert.Equal(new ModelLayout(256, false, 0, 1), layout);
    }

    [Fact]
    public void ReadsAnNchwModelWithTheLandmarksNotFirst()
    {
        var layout = ModelLayout.Parse(new[] { 1, 3, 192, 192 }, new[] { new[] { 1 }, new[] { 1, 1404 } });
        Assert.Equal(new ModelLayout(192, true, 1, 0), layout);
    }

    [Fact]
    public void DynamicSizeFallsBackToTheDefault()
    {
        var layout = ModelLayout.Parse(new[] { 1, -1, -1, 3 }, new[] { new[] { 1, 1434 } });
        Assert.Equal(256, layout!.Size);
        Assert.Equal(-1, layout.ScoreOutput);
    }

    [Fact]
    public void RefusesUnusableModels()
    {
        Assert.Null(ModelLayout.Parse(new[] { 1, 256, 3 }, new[] { new[] { 1, 1434 } }));
        Assert.Null(ModelLayout.Parse(new[] { 1, 256, 256, 1 }, new[] { new[] { 1, 1434 } }));
        Assert.Null(ModelLayout.Parse(new[] { 1, 256, 256, 3 }, new[] { new[] { 1, 10 } }));
    }
}
