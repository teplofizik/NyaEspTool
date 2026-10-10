namespace Esp32Flasher.Core;

public sealed record FlashImage(uint Offset, byte[] Data, string? Name = null)
{
    public static FlashImage FromFile(uint offset, string path)
    {
        byte[] data = File.ReadAllBytes(path);
        return new FlashImage(offset, data, Path.GetFileName(path));
    }
}
