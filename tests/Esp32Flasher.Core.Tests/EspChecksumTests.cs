using Esp32Flasher.Core.Protocol;

namespace Esp32Flasher.Core.Tests;

public class EspChecksumTests
{
    [Fact]
    public void Compute_EmptyPayload_ReturnsSeed()
    {
        Assert.Equal(EspChecksum.Seed, EspChecksum.Compute(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Compute_XorsEachByteStartingFromSeed()
    {
        byte[] data = { 0x00, 0x01, 0x02 };

        Assert.Equal(0xEC, EspChecksum.Compute(data));
    }

    [Fact]
    public void Compute_IsInvolution_OverKnownData()
    {
        byte[] data = { 0x10, 0x20, 0x30, 0x40 };

        byte first = EspChecksum.Compute(data);
        byte second = EspChecksum.Compute(data);

        Assert.Equal(first, second);
    }
}
