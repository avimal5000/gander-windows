using System.Security.Cryptography;

namespace Sway.Core;

/// <summary>
/// The pinned identity of the face-landmark model. The same SHA-256 is pinned in <c>scripts/fetch-pose-model.sh</c>
/// (<c>scripts/privacy-guard.sh</c> fails when the two differ). Gander hashes the model bytes before handing them to ONNX
/// Runtime, so a file swapped in the install folder is refused instead of parsed by native code.
/// </summary>
public static class ModelIntegrity
{
    /// <summary>Lowercase hex SHA-256 of <c>face_landmark.onnx</c>.</summary>
    public const string PinnedSha256 = "d16e5a55e6a480284d468ee32469692464049518c311662ab3956681de31e3e9";

    /// <summary>The lowercase hex SHA-256 of the bytes.</summary>
    public static string Hash(ReadOnlySpan<byte> model) => Convert.ToHexString(SHA256.HashData(model)).ToLowerInvariant();

    /// <summary>True when the bytes are the pinned model; otherwise <paramref name="message"/> says why not (numbers only).</summary>
    public static bool Verify(ReadOnlySpan<byte> model, out string message)
    {
        var actual = Hash(model);
        if (string.Equals(actual, PinnedSha256, StringComparison.Ordinal))
        {
            message = string.Empty;
            return true;
        }

        message = $"The face model does not match the pinned SHA-256 (expected {PinnedSha256}, found {actual}). Reinstall Gander.";
        return false;
    }
}
