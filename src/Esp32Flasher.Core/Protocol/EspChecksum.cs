namespace Esp32Flasher.Core.Protocol;

public static class EspChecksum
{
    public const byte Seed = 0xEF;

    public static byte Compute(ReadOnlySpan<byte> data)
    {
        byte result = Seed;
        foreach (byte value in data)
        {
            result ^= value;
        }

        return result;
    }
}
