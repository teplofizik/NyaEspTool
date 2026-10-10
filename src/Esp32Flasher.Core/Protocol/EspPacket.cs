using System.Buffers.Binary;

namespace Esp32Flasher.Core.Protocol;

public static class EspPacket
{
    public const byte DirectionRequest = 0x00;
    public const byte DirectionResponse = 0x01;
    public const int HeaderSize = 8;

    public static byte[] Build(EspCommand command, ReadOnlySpan<byte> data = default, byte checksum = 0)
    {
        if (data.Length > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(data), data.Length, "Command payload exceeds 65535 bytes.");
        }

        var packet = new byte[HeaderSize + data.Length];
        packet[0] = DirectionRequest;
        packet[1] = (byte)command;
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), (ushort)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4, 4), checksum);
        data.CopyTo(packet.AsSpan(HeaderSize));
        return packet;
    }
}
