namespace Sway.Core;

/// <summary>A square region of a camera frame, in frame pixels. The center and side may fall partly outside the frame.</summary>
public readonly record struct SquareBox(double CenterX, double CenterY, double Side)
{
    /// <summary>Left edge.</summary>
    public double Left => CenterX - (Side / 2);

    /// <summary>Top edge.</summary>
    public double Top => CenterY - (Side / 2);

    /// <summary>The smallest square around a rectangle, enlarged by <paramref name="scale"/> (MediaPipe uses about 1.5).</summary>
    public static SquareBox AroundRectangle(double left, double top, double width, double height, double scale) =>
        new(left + (width / 2), top + (height / 2), Math.Max(width, height) * scale);

    /// <summary>The tracking box for the next frame: the landmarks' bounds, squared and enlarged by <paramref name="scale"/>.</summary>
    public static SquareBox? FromLandmarks(IReadOnlyList<Landmark> landmarks, double scale)
    {
        if (landmarks.Count == 0)
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

        var box = AroundRectangle(minX, minY, maxX - minX, maxY - minY, scale);
        return box.Side > 1 ? box : null;
    }
}

/// <summary>
/// Pure image helpers for the landmark model: crop a square from a BGRA frame, resize it bilinearly, and write it as
/// RGB floats in [0, 1]. Written by hand over the bytes so no imaging library is needed. Pixels outside the frame are black.
/// </summary>
public static class FaceCrop
{
    /// <summary>
    /// Fills <paramref name="destination"/> (length size * size * 3) with the crop resized to size x size. NHWC order is
    /// [y][x][channel]; NCHW is [channel][y][x]. Channels are R, G, B.
    /// </summary>
    public static void FillTensor(ReadOnlySpan<byte> bgra, int width, int height, SquareBox box, int size, bool channelsFirst, Span<float> destination)
    {
        if (destination.Length < size * size * 3 || bgra.Length < width * height * 4)
        {
            throw new ArgumentException("Buffer too small for the stated frame or tensor size.");
        }

        var step = box.Side / size;
        var plane = size * size;
        for (var y = 0; y < size; y++)
        {
            var sourceY = box.Top + (((y + 0.5) * step) - 0.5);
            for (var x = 0; x < size; x++)
            {
                var sourceX = box.Left + (((x + 0.5) * step) - 0.5);
                Sample(bgra, width, height, sourceX, sourceY, out var r, out var g, out var b);
                if (channelsFirst)
                {
                    var at = (y * size) + x;
                    destination[at] = r;
                    destination[plane + at] = g;
                    destination[(2 * plane) + at] = b;
                }
                else
                {
                    var at = ((y * size) + x) * 3;
                    destination[at] = r;
                    destination[at + 1] = g;
                    destination[at + 2] = b;
                }
            }
        }
    }

    /// <summary>
    /// Maps landmarks from model crop pixels (0..size) back to frame pixels. z is scaled like x and y, as MediaPipe does.
    /// </summary>
    public static Landmark[] MapToFrame(ReadOnlySpan<float> flatXyz, int size, SquareBox box)
    {
        var count = flatXyz.Length / 3;
        var scale = box.Side / size;
        var mapped = new Landmark[count];
        for (var i = 0; i < count; i++)
        {
            mapped[i] = new Landmark(
                box.Left + (flatXyz[i * 3] * scale),
                box.Top + (flatXyz[(i * 3) + 1] * scale),
                flatXyz[(i * 3) + 2] * scale);
        }

        return mapped;
    }

    /// <summary>The logistic function. A face-presence output that is not already in 0..1 is a logit and goes through this.</summary>
    public static double Sigmoid(double logit) => 1.0 / (1.0 + Math.Exp(-logit));

    private static void Sample(ReadOnlySpan<byte> bgra, int width, int height, double x, double y, out float r, out float g, out float b)
    {
        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);
        var fx = (float)(x - x0);
        var fy = (float)(y - y0);
        float sr = 0, sg = 0, sb = 0;
        for (var dy = 0; dy <= 1; dy++)
        {
            for (var dx = 0; dx <= 1; dx++)
            {
                var px = x0 + dx;
                var py = y0 + dy;
                if (px < 0 || py < 0 || px >= width || py >= height)
                {
                    continue; // black outside the frame
                }

                var weight = (dx == 0 ? 1 - fx : fx) * (dy == 0 ? 1 - fy : fy);
                var at = ((py * width) + px) * 4;
                sb += bgra[at] * weight;
                sg += bgra[at + 1] * weight;
                sr += bgra[at + 2] * weight;
            }
        }

        r = sr / 255f;
        g = sg / 255f;
        b = sb / 255f;
    }
}

/// <summary>What the landmark model's metadata says about its tensors, worked out from names and shapes alone.</summary>
/// <param name="Size">Square input side in pixels.</param>
/// <param name="ChannelsFirst">True for NCHW input, false for NHWC.</param>
/// <param name="LandmarkOutput">Index of the output holding flat x, y, z landmarks.</param>
/// <param name="ScoreOutput">Index of the face-presence output, or -1 when the model has none.</param>
public sealed record ModelLayout(int Size, bool ChannelsFirst, int LandmarkOutput, int ScoreOutput)
{
    /// <summary>The input size assumed when the shape is dynamic.</summary>
    public const int DefaultSize = 256;

    /// <summary>
    /// Reads the layout. A dimension of zero or less counts as dynamic. Returns null when the input is not a
    /// 4-D image tensor with 3 channels or no output can hold at least 468 landmarks.
    /// </summary>
    public static ModelLayout? Parse(IReadOnlyList<int> inputShape, IReadOnlyList<IReadOnlyList<int>> outputShapes)
    {
        if (inputShape.Count != 4)
        {
            return null;
        }

        bool channelsFirst;
        if (inputShape[3] == 3)
        {
            channelsFirst = false;
        }
        else if (inputShape[1] == 3)
        {
            channelsFirst = true;
        }
        else
        {
            return null;
        }

        var side = channelsFirst ? inputShape[2] : inputShape[1];
        var size = side > 0 ? side : DefaultSize;

        var landmark = -1;
        var best = 0;
        for (var i = 0; i < outputShapes.Count; i++)
        {
            var elements = Elements(outputShapes[i]);
            if (elements >= PoseGeometry.MinimumLandmarks * 3 && elements % 3 == 0 && elements > best)
            {
                best = elements;
                landmark = i;
            }
        }

        if (landmark < 0)
        {
            return null;
        }

        var score = -1;
        for (var i = 0; i < outputShapes.Count; i++)
        {
            if (i != landmark && Elements(outputShapes[i]) == 1)
            {
                score = i; // the first single-value output after the landmarks is the face-presence score
                break;
            }
        }

        return new ModelLayout(size, channelsFirst, landmark, score);
    }

    private static int Elements(IReadOnlyList<int> shape) => shape.Aggregate(1, (product, dimension) => product * Math.Max(dimension, 1));
}
