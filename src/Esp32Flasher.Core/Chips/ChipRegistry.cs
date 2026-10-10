namespace Esp32Flasher.Core.Chips;

public static class ChipRegistry
{
    public static ChipConfig Esp32 { get; } = new()
    {
        Name = "ESP32",
        ImageChipId = 0,
        MagicValue = Esp32Chip.MagicValue,
        BootloaderOffset = 0x1000,
        SpiRegBase = 0x3FF42000,
        SpiUsrOffs = 0x1C,
        SpiUsr2Offs = 0x24,
        SpiMisoDlenOffs = 0x2C,
        SpiW0Offs = 0x80,
        StubResourceName = "Esp32Flasher.Core.Resources.esp32_stub.json",
        UsesSecurityInfo = false,
        MacMode = MacReadMode.Esp32EfuseWords,
        Revision = RevisionScheme.Esp32,
        EfuseReadBase = 0x3FF5A000,
    };

    public static ChipConfig Esp32S3 { get; } = new()
    {
        Name = "ESP32-S3",
        ImageChipId = 9,
        MagicValue = null,
        BootloaderOffset = 0x0,
        SpiRegBase = 0x60002000,
        SpiUsrOffs = 0x18,
        SpiUsr2Offs = 0x20,
        SpiMisoDlenOffs = 0x28,
        SpiW0Offs = 0x58,
        StubResourceName = "Esp32Flasher.Core.Resources.esp32s3_stub.json",
        UsesSecurityInfo = true,
        MacMode = MacReadMode.MacRegister,
        Revision = RevisionScheme.Esp32S3,
        MacRegister = 0x60007044,
        EfuseBase = 0x60007000,
    };

    public static ChipConfig Esp32C6 { get; } = new()
    {
        Name = "ESP32-C6",
        ImageChipId = 13,
        MagicValue = null,
        BootloaderOffset = 0x0,
        SpiRegBase = 0x60003000,
        SpiUsrOffs = 0x18,
        SpiUsr2Offs = 0x20,
        SpiMisoDlenOffs = 0x28,
        SpiW0Offs = 0x58,
        StubResourceName = "Esp32Flasher.Core.Resources.esp32c6_stub.json",
        UsesSecurityInfo = true,
        MacMode = MacReadMode.MacRegister,
        Revision = RevisionScheme.Esp32C6,
        MacRegister = 0x600B0844,
        EfuseBase = 0x600B0800,
    };

    public static ChipConfig Esp32C3 { get; } = new()
    {
        Name = "ESP32-C3",
        ImageChipId = 5,
        MagicValue = null,
        BootloaderOffset = 0x0,
        SpiRegBase = 0x60002000,
        SpiUsrOffs = 0x18,
        SpiUsr2Offs = 0x20,
        SpiMisoDlenOffs = 0x28,
        SpiW0Offs = 0x58,
        StubResourceName = "Esp32Flasher.Core.Resources.esp32c3_stub.json",
        UsesSecurityInfo = true,
        MacMode = MacReadMode.MacRegister,
        Revision = RevisionScheme.Esp32C3,
        MacRegister = 0x60008844,
        EfuseBase = 0x60008800,
    };

    public static IReadOnlyList<ChipConfig> All { get; } = new[] { Esp32, Esp32S3, Esp32C6, Esp32C3 };

    public static ChipConfig? FromMagic(uint magicValue)
        => All.FirstOrDefault(chip => chip.MagicValue == magicValue);

    public static ChipConfig? FromChipId(uint chipId)
        => All.FirstOrDefault(chip => chip.UsesSecurityInfo && chip.ImageChipId == chipId);
}
