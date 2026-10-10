namespace Esp32Flasher.Core.Chips;

public static class Esp32Chip
{
    public const uint MagicAddress = 0x40001000;
    public const uint MagicValue = 0x00F01D83;

    public const uint EfuseBase = 0x3FF5A000;
    public const uint ApbCtlDateAddress = 0x3FF66000 + 0x7C;

    public const uint BootloaderOffset = 0x1000;

    public const uint SpiRegBase = 0x3FF42000;
    public const int SpiUsrOffs = 0x1C;
    public const int SpiUsr1Offs = 0x20;
    public const int SpiUsr2Offs = 0x24;
    public const int SpiMosiDlenOffs = 0x28;
    public const int SpiMisoDlenOffs = 0x2C;
    public const int SpiW0Offs = 0x80;

    public const int RamBlockSize = 0x1800;

    public static uint EfuseWord(int index) => EfuseBase + (uint)(4 * index);

    public static uint? DecodeFlashSizeBytes(uint flashId)
    {
        uint sizeId = (flashId >> 16) & 0xFF;
        return sizeId switch
        {
            0x12 or 0x32 => 256u * 1024,
            0x13 or 0x33 => 512u * 1024,
            0x14 or 0x34 => 1024u * 1024,
            0x15 or 0x35 => 2u * 1024 * 1024,
            0x16 or 0x36 => 4u * 1024 * 1024,
            0x17 or 0x37 => 8u * 1024 * 1024,
            0x18 or 0x38 => 16u * 1024 * 1024,
            0x19 or 0x39 => 32u * 1024 * 1024,
            0x1A or 0x3A => 64u * 1024 * 1024,
            0x1B or 0x3B => 128u * 1024 * 1024,
            0x1C or 0x3C => 256u * 1024 * 1024,
            0x20 => 64u * 1024 * 1024,
            0x21 => 128u * 1024 * 1024,
            0x22 => 256u * 1024 * 1024,
            _ => null,
        };
    }

    public static Imaging.FlashSize? SizeFromBytes(uint bytes) => bytes switch
    {
        <= 1u * 1024 * 1024 => Imaging.FlashSize.MB1,
        <= 2u * 1024 * 1024 => Imaging.FlashSize.MB2,
        <= 4u * 1024 * 1024 => Imaging.FlashSize.MB4,
        <= 8u * 1024 * 1024 => Imaging.FlashSize.MB8,
        <= 16u * 1024 * 1024 => Imaging.FlashSize.MB16,
        <= 32u * 1024 * 1024 => Imaging.FlashSize.MB32,
        <= 64u * 1024 * 1024 => Imaging.FlashSize.MB64,
        _ => Imaging.FlashSize.MB128,
    };
}
