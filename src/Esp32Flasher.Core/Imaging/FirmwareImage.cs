using System.Buffers.Binary;

namespace Esp32Flasher.Core.Imaging;

public enum FlashMode : byte
{
    Qio = 0,
    Qout = 1,
    Dio = 2,
    Dout = 3,
}

public sealed record ImageSegment(uint Offset, byte[] Data);

public sealed class FirmwareImage
{
    private FirmwareImage(
        FlashMode flashMode,
        byte flashSizeCode,
        byte flashFreqCode,
        uint entryPoint,
        ushort chipId,
        bool hashAppended,
        IReadOnlyList<ImageSegment> segments,
        byte checksum,
        byte[]? sha256)
    {
        FlashMode = flashMode;
        FlashSizeCode = flashSizeCode;
        FlashFreqCode = flashFreqCode;
        EntryPoint = entryPoint;
        ChipId = chipId;
        HashAppended = hashAppended;
        Segments = segments;
        Checksum = checksum;
        Sha256 = sha256;
    }

    public const byte Magic = 0xE9;

    public FlashMode FlashMode { get; }

    public byte FlashSizeCode { get; }

    public byte FlashFreqCode { get; }

    public uint EntryPoint { get; }

    public ushort ChipId { get; }

    public bool HashAppended { get; }

    public IReadOnlyList<ImageSegment> Segments { get; }

    public byte Checksum { get; }

    public byte[]? Sha256 { get; }

    public int SegmentCount => Segments.Count;

    public static FirmwareImage FromFile(string path) => Parse(File.ReadAllBytes(path));

    public static FirmwareImage Parse(byte[] data)
    {
        if (data.Length < 24)
        {
            throw new FormatException("Image is too small to contain a header.");
        }

        if (data[0] != Magic)
        {
            throw new FormatException($"Invalid image magic 0x{data[0]:X2} (expected 0xE9).");
        }

        int segmentCount = data[1];
        var flashMode = (FlashMode)data[2];
        byte flashSizeCode = (byte)(data[3] >> 4);
        byte flashFreqCode = (byte)(data[3] & 0x0F);
        uint entryPoint = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4, 4));
        ushort chipId = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(12, 2));
        bool hashAppended = data[23] == 0x01;

        var segments = new List<ImageSegment>(segmentCount);
        int position = 24;
        for (int i = 0; i < segmentCount; i++)
        {
            if (position + 8 > data.Length)
            {
                throw new FormatException("Truncated segment header.");
            }

            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position, 4));
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position + 4, 4));
            position += 8;

            if (position + size > data.Length)
            {
                throw new FormatException("Truncated segment data.");
            }

            byte[] segment = data[position..(int)(position + size)];
            position += (int)size;
            segments.Add(new ImageSegment(offset, segment));
        }

        int footerEnd = data.Length - (hashAppended ? 32 : 0);
        byte checksum = data[footerEnd - 1];
        byte[]? sha256 = hashAppended ? data[^32..] : null;

        return new FirmwareImage(
            flashMode, flashSizeCode, flashFreqCode, entryPoint, chipId, hashAppended, segments, checksum, sha256);
    }

    public byte ComputeChecksum()
    {
        byte result = 0xEF;
        foreach (ImageSegment segment in Segments)
        {
            foreach (byte value in segment.Data)
            {
                result ^= value;
            }
        }

        return result;
    }

    public string FlashSizeName => FlashSizeCode switch
    {
        0 => "1MB",
        1 => "2MB",
        2 => "4MB",
        3 => "8MB",
        4 => "16MB",
        5 => "32MB",
        6 => "64MB",
        7 => "128MB",
        _ => "unknown",
    };

    public string FlashFreqName => FlashFreqCode switch
    {
        0 => "40MHz",
        1 => "26MHz",
        2 => "20MHz",
        0x0F => "80MHz",
        _ => "unknown",
    };
}
