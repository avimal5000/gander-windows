namespace Gander.Core.Tests;

public class ModelIntegrityTests
{
    [Fact]
    public void HashIsLowercaseSha256Hex()
    {
        // SHA-256 of the three bytes "abc".
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", ModelIntegrity.Hash("abc"u8));
    }

    [Fact]
    public void AnythingButThePinnedModelIsRefusedWithAMessage()
    {
        Assert.False(ModelIntegrity.Verify("not a model"u8, out var message));
        Assert.Contains(ModelIntegrity.PinnedSha256, message, StringComparison.Ordinal);
        Assert.False(ModelIntegrity.Verify(ReadOnlySpan<byte>.Empty, out _));
    }

    [Fact]
    public void ThePinMatchesTheFetchScript()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "scripts", "fetch-pose-model.sh")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        var script = File.ReadAllText(Path.Combine(dir!, "scripts", "fetch-pose-model.sh"));
        Assert.Contains($"SHA256=\"{ModelIntegrity.PinnedSha256}\"", script, StringComparison.Ordinal);
    }
}
