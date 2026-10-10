using System.Buffers;

namespace Esp32Flasher.Core.Slip;

public static class SlipCodec
{
    public const byte End = 0xC0;
    public const byte Esc = 0xDB;
    public const byte EscEnd = 0xDC;
    public const byte EscEsc = 0xDD;

    public static byte[] Encode(ReadOnlySpan<byte> payload)
    {
        var writer = new ArrayBufferWriter<byte>(payload.Length + 4);
        Span<byte> span = writer.GetSpan((payload.Length * 2) + 2);

        int written = 0;
        span[written++] = End;
        foreach (byte value in payload)
        {
            switch (value)
            {
                case End:
                    span[written++] = Esc;
                    span[written++] = EscEnd;
                    break;
                case Esc:
                    span[written++] = Esc;
                    span[written++] = EscEsc;
                    break;
                default:
                    span[written++] = value;
                    break;
            }
        }

        span[written++] = End;
        writer.Advance(written);
        return writer.WrittenSpan.ToArray();
    }

    public static bool TryDecode(ReadOnlySpan<byte> buffer, out byte[] packet, out int consumed)
    {
        packet = Array.Empty<byte>();
        consumed = 0;

        int start = buffer.IndexOf(End);
        if (start < 0)
        {
            return false;
        }

        var frame = new List<byte>();
        bool escaped = false;
        for (int i = start + 1; i < buffer.Length; i++)
        {
            byte value = buffer[i];
            if (value == End)
            {
                packet = frame.ToArray();
                consumed = i + 1;
                return true;
            }

            if (escaped)
            {
                frame.Add(value switch
                {
                    EscEnd => End,
                    EscEsc => Esc,
                    _ => value,
                });
                escaped = false;
                continue;
            }

            if (value == Esc)
            {
                escaped = true;
                continue;
            }

            frame.Add(value);
        }

        return false;
    }
}
