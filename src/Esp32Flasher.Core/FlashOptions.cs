namespace Esp32Flasher.Core;

using Esp32Flasher.Core.Imaging;

public sealed record FlashOptions
{
    public uint PacketSizeBytes { get; init; } = 16 * 1024;

    public uint FlashSizeBytes { get; init; } = 4 * 1024 * 1024;

    public bool Verify { get; init; } = true;

    public bool Reboot { get; init; } = true;

    public int MaxRetriesPerBlock { get; init; } = 3;

    public FlashMode? Mode { get; init; }

    public FlashFrequency? Frequency { get; init; }

    public FlashSize? Size { get; init; }
}
