namespace Esp32Flasher.Core.Chips;

public enum MacReadMode
{
    Esp32EfuseWords,
    MacRegister,
}

public sealed record ChipConfig
{
    public required string Name { get; init; }

    public required uint ImageChipId { get; init; }

    public uint? MagicValue { get; init; }

    public required uint BootloaderOffset { get; init; }

    public required uint SpiRegBase { get; init; }

    public required int SpiUsrOffs { get; init; }

    public required int SpiUsr2Offs { get; init; }

    public required int SpiMisoDlenOffs { get; init; }

    public required int SpiW0Offs { get; init; }

    public required string StubResourceName { get; init; }

    public required bool UsesSecurityInfo { get; init; }

    public required MacReadMode MacMode { get; init; }

    public uint MacRegister { get; init; }

    public uint EfuseReadBase { get; init; }

    public int RamBlockSize { get; init; } = 0x1800;
}
