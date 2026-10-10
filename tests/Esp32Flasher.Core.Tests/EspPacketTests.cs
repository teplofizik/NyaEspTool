using Esp32Flasher.Core.Protocol;

namespace Esp32Flasher.Core.Tests;

public class EspPacketTests
{
    [Fact]
    public void Build_ReadReg_WritesHeaderAndPayload()
    {
        byte[] address = { 0x00, 0x10, 0x00, 0x40 };

        byte[] packet = EspPacket.Build(EspCommand.ReadReg, address);

        Assert.Equal(
            new byte[] { 0x00, 0x0A, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x40 },
            packet);
    }

    [Fact]
    public void Build_WithoutData_HasZeroSize()
    {
        byte[] packet = EspPacket.Build(EspCommand.Sync);

        Assert.Equal(EspPacket.HeaderSize, packet.Length);
        Assert.Equal(0x00, packet[0]);
        Assert.Equal((byte)EspCommand.Sync, packet[1]);
        Assert.Equal(0x00, packet[2]);
        Assert.Equal(0x00, packet[3]);
    }

    [Fact]
    public void Build_StoresChecksumInHeader()
    {
        byte[] packet = EspPacket.Build(EspCommand.FlashData, new byte[] { 0x01 }, checksum: 0xAB);

        Assert.Equal(0xAB, packet[4]);
        Assert.Equal(0x00, packet[5]);
    }

    [Fact]
    public void Build_TooLargePayload_Throws()
    {
        byte[] tooLarge = new byte[ushort.MaxValue + 1];

        Assert.Throws<ArgumentOutOfRangeException>(() => EspPacket.Build(EspCommand.FlashData, tooLarge));
    }
}
