using System.Security.Cryptography;

namespace Esp32Flasher.Core.Imaging;

public enum FlashFrequency : byte
{
    MHz40 = 0,
    MHz26 = 1,
    MHz20 = 2,
    MHz80 = 0x0F,
}

public enum FlashSize : byte
{
    MB1 = 0,
    MB2 = 1,
    MB4 = 2,
    MB8 = 3,
    MB16 = 4,
    MB32 = 5,
    MB64 = 6,
    MB128 = 7,
}

public static class ImageFlashParams
{
    public const byte Magic = 0xE9;
    public const int ExtendedHeaderHashOffset = 23;

    public static byte[] Apply(
        byte[] image,
        uint address,
        FlashMode? mode,
        FlashFrequency? frequency,
        FlashSize? size,
        uint bootloaderOffset)
    {
        if (image.Length < 24 || address != bootloaderOffset)
        {
            return image;
        }

        if (mode is null && frequency is null && size is null)
        {
            return image;
        }

        if (image[0] != Magic)
        {
            return image;
        }

        bool shaAppended = image[ExtendedHeaderHashOffset] == 0x01;

        byte newMode = mode is not null ? (byte)mode : image[2];
        byte frequencyBits = frequency is not null ? (byte)frequency : (byte)(image[3] & 0x0F);
        byte sizeBits = size is not null ? (byte)((byte)size << 4) : (byte)(image[3] & 0xF0);
        byte newSizeFreq = (byte)(sizeBits | frequencyBits);

        if (image[2] == newMode && image[3] == newSizeFreq)
        {
            return image;
        }

        byte[] result = (byte[])image.Clone();
        result[2] = newMode;
        result[3] = newSizeFreq;

        if (shaAppended)
        {
            int dataLength = result.Length - 32;
            byte[] digest = SHA256.HashData(result.AsSpan(0, dataLength));
            digest.CopyTo(result, dataLength);
        }

        return result;
    }
}
