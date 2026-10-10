namespace Esp32Flasher.Core.Chips;

public enum MacReadMode
{
    Esp32EfuseWords,
    MacRegister,
}

public enum RevisionScheme
{
    Esp32,
    Esp32S3,
    Esp32C3,
    Esp32C6,
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

    public required RevisionScheme Revision { get; init; }

    public uint MacRegister { get; init; }

    public uint EfuseReadBase { get; init; }

    public uint EfuseBase { get; init; }

    public int RamBlockSize { get; init; } = 0x1800;
}
