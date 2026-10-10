using Esp32Flasher.Core.Protocol;
using Esp32Flasher.Core.Slip;

namespace Esp32Flasher.Core.Tests;

public class EspResponseTests
{
    [Fact]
    public void Parse_ReadReg_ReturnsMagicValue()
    {
        byte[] payload =
        {
            0x01, 0x0A, 0x04, 0x00,
            0x83, 0x1D, 0xF0, 0x00,
            0x00, 0x00, 0x00, 0x00,
        };

        EspResponse response = EspResponse.Parse(payload);

        Assert.Equal(EspPacket.DirectionResponse, response.Direction);
        Assert.Equal(EspCommand.ReadReg, response.Command);
        Assert.Equal(0x00F01D83u, response.Value);
        Assert.Equal(4, response.Size);
    }

    [Fact]
    public void GetStatus_Rom_ReadsFourthByteFromEnd()
    {
        byte[] payload =
        {
            0x01, 0x02, 0x08, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        };

        EspResponse response = EspResponse.Parse(payload);
        EspResponseStatus status = response.GetStatus(ResponseStatusFormat.Rom);

        Assert.True(status.Success);
        Assert.Equal(0x00, status.Error);
    }

    [Fact]
    public void GetStatus_Rom_ReportsErrorCode()
    {
        byte[] payload =
        {
            0x01, 0x02, 0x08, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x01, 0x08, 0x00, 0x00,
        };

        EspResponse response = EspResponse.Parse(payload);
        EspResponseStatus status = response.GetStatus(ResponseStatusFormat.Rom);

        Assert.False(status.Success);
        Assert.Equal(0x08, status.Error);
    }

    [Fact]
    public void GetStatus_Stub_ReadsLastTwoBytes()
    {
        byte[] payload =
        {
            0x01, 0x02, 0x04, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x01, 0xC2,
        };

        EspResponse response = EspResponse.Parse(payload);
        EspResponseStatus status = response.GetStatus(ResponseStatusFormat.Stub);

        Assert.False(status.Success);
        Assert.Equal(0xC2, status.Error);
    }

    [Fact]
    public void Parse_SyncResponseFromTrace()
    {
        byte[] decode = SlipCodec.TryDecode(
            new byte[] { 0xC0, 0x01, 0x08, 0x04, 0x00, 0x07, 0x12, 0x55, 0x55, 0x00, 0x00, 0x00, 0x00, 0xC0 },
            out byte[] payload,
            out _)
            ? payload
            : Array.Empty<byte>();

        EspResponse response = EspResponse.Parse(decode);

        Assert.Equal(EspCommand.Sync, response.Command);
        Assert.Equal(4, response.Size);
    }

    [Fact]
    public void TryParse_RejectsWrongDirection()
    {
        byte[] payload =
        {
            0x00, 0x0A, 0x04, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
        };

        Assert.False(EspResponse.TryParse(payload, out _));
    }

    [Fact]
    public void TryParse_RejectsTruncatedPayload()
    {
        byte[] payload =
        {
            0x01, 0x0A, 0x08, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00,
        };

        Assert.False(EspResponse.TryParse(payload, out _));
    }
}
