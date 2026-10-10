using Esp32Flasher.Core.Slip;

namespace Esp32Flasher.Core.Tests;

public class SlipCodecTests
{
    [Fact]
    public void Encode_WrapsPayloadWithDelimiters()
    {
        byte[] payload = { 0x01, 0x02, 0x03 };

        byte[] encoded = SlipCodec.Encode(payload);

        Assert.Equal(SlipCodec.End, encoded[0]);
        Assert.Equal(SlipCodec.End, encoded[^1]);
        Assert.Equal(payload, encoded[1..^1]);
    }

    [Fact]
    public void Encode_EscapesSpecialBytes()
    {
        byte[] payload = { SlipCodec.End, SlipCodec.Esc, 0x00 };

        byte[] encoded = SlipCodec.Encode(payload);

        Assert.Equal(
            new byte[]
            {
                SlipCodec.End,
                SlipCodec.Esc, SlipCodec.EscEnd,
                SlipCodec.Esc, SlipCodec.EscEsc,
                0x00,
                SlipCodec.End,
            },
            encoded);
    }

    [Fact]
    public void RoundTrip_ThroughDecoder_ReturnsOriginalPayload()
    {
        byte[] payload = { 0x00, SlipCodec.End, 0xAB, SlipCodec.Esc, SlipCodec.End, 0xFF };
        byte[] encoded = SlipCodec.Encode(payload);

        var decoder = new SlipDecoder();
        IReadOnlyList<byte[]> frames = decoder.Feed(encoded);

        byte[] frame = Assert.Single(frames);
        Assert.Equal(payload, frame);
    }

    [Fact]
    public void TryDecode_ExtractsFrameAndConsumedCount()
    {
        byte[] payload = { 0x0A, 0x0B, 0x0C };
        byte[] encoded = SlipCodec.Encode(payload);
        byte[] buffer = [0x99, 0x88, .. encoded, 0x77];

        bool ok = SlipCodec.TryDecode(buffer, out byte[] packet, out int consumed);

        Assert.True(ok);
        Assert.Equal(payload, packet);
        Assert.Equal(buffer.Length - 1, consumed);
    }

    [Fact]
    public void Decoder_IgnoresGarbageBeforeFirstDelimiter()
    {
        byte[] stream = { 0x12, 0x34, SlipCodec.End, 0x01, 0x02, SlipCodec.End };

        var decoder = new SlipDecoder();
        IReadOnlyList<byte[]> frames = decoder.Feed(stream);

        byte[] frame = Assert.Single(frames);
        Assert.Equal(new byte[] { 0x01, 0x02 }, frame);
    }

    [Fact]
    public void Decoder_ReassemblesFramesAcrossChunks()
    {
        byte[] payload = { 0xDE, 0xAD, SlipCodec.End, 0xBE, 0xEF };
        byte[] encoded = SlipCodec.Encode(payload);

        var decoder = new SlipDecoder();
        var received = new List<byte[]>();
        foreach (byte b in encoded)
        {
            received.AddRange(decoder.Feed(new[] { b }));
        }

        byte[] frame = Assert.Single(received);
        Assert.Equal(payload, frame);
    }

    [Fact]
    public void Decoder_HandlesConsecutiveDelimiters()
    {
        byte[] stream =
        {
            SlipCodec.End,
            0x01, SlipCodec.End,
            SlipCodec.End,
            0x02, SlipCodec.End,
        };

        var decoder = new SlipDecoder();
        IReadOnlyList<byte[]> frames = decoder.Feed(stream);

        Assert.Equal(2, frames.Count);
        Assert.Equal(new byte[] { 0x01 }, frames[0]);
        Assert.Equal(new byte[] { 0x02 }, frames[1]);
    }
}
