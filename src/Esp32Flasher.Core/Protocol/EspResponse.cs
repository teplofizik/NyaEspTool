using System.Buffers.Binary;

namespace Esp32Flasher.Core.Protocol;

public sealed class EspResponse
{
    private readonly byte[] _data;

    private EspResponse(byte direction, EspCommand command, uint value, byte[] data)
    {
        Direction = direction;
        Command = command;
        Value = value;
        _data = data;
    }

    public byte Direction { get; }

    public EspCommand Command { get; }

    public ushort Size => (ushort)_data.Length;

    public uint Value { get; }

    public ReadOnlyMemory<byte> Data => _data;

    public static EspResponse Parse(ReadOnlySpan<byte> payload)
    {
        if (!TryParse(payload, out EspResponse? response))
        {
            throw new FormatException("Invalid ESP response packet.");
        }

        return response;
    }

    public static bool TryParse(ReadOnlySpan<byte> payload, out EspResponse response)
    {
        response = null!;

        if (payload.Length < EspPacket.HeaderSize || payload[0] != EspPacket.DirectionResponse)
        {
            return false;
        }

        ushort size = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(2, 2));
        if (payload.Length < EspPacket.HeaderSize + size)
        {
            return false;
        }

        var command = (EspCommand)payload[1];
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, 4));
        byte[] data = payload.Slice(EspPacket.HeaderSize, size).ToArray();
        response = new EspResponse(payload[0], command, value, data);
        return true;
    }

    public EspResponseStatus GetStatus(ResponseStatusFormat format)
    {
        int statusIndex = format == ResponseStatusFormat.Rom ? _data.Length - 4 : _data.Length - 2;
        if (statusIndex < 0 || statusIndex + 1 >= _data.Length)
        {
            throw new InvalidOperationException("Response payload is too short to contain a status.");
        }

        return new EspResponseStatus(_data[statusIndex], _data[statusIndex + 1]);
    }
}

public enum ResponseStatusFormat
{
    Stub,
    Rom,
}

public readonly record struct EspResponseStatus(byte Status, byte Error)
{
    public bool Success => Status == 0;
}
