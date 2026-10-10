using System.Buffers.Binary;
using Esp32Flasher.Core.Imaging;

namespace Esp32Flasher.Core.Tests;

public class FirmwareImageTests
{
    private static byte[] BuildImage()
    {
        byte[] header = new byte[24];
        header[0] = 0xE9;
        header[1] = 1;
        header[2] = (byte)FlashMode.Dio;
        header[3] = 0x2F;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 0x40080000);
        header[23] = 0x00;

        byte[] segment = { 0x01, 0x02, 0x03, 0x04 };
        byte[] segmentHeader = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(segmentHeader.AsSpan(0), 0x3F400000);
        BinaryPrimitives.WriteUInt32LittleEndian(segmentHeader.AsSpan(4), (uint)segment.Length);

        var body = new List<byte>();
        body.AddRange(header);
        body.AddRange(segmentHeader);
        body.AddRange(segment);
        while (body.Count % 16 != 15)
        {
            body.Add(0x00);
        }

        body.Add(0xEB);
        return body.ToArray();
    }

    [Fact]
    public void Parse_ReadsHeaderAndSegments()
    {
        FirmwareImage image = FirmwareImage.Parse(BuildImage());

        Assert.Equal(0x40080000u, image.EntryPoint);
        Assert.Equal(FlashMode.Dio, image.FlashMode);
        Assert.Equal("4MB", image.FlashSizeName);
        Assert.Equal("80MHz", image.FlashFreqName);
        Assert.False(image.HashAppended);
        Assert.Null(image.Sha256);

        ImageSegment segment = Assert.Single(image.Segments);
        Assert.Equal(0x3F400000u, segment.Offset);
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04 }, segment.Data);
    }

    [Fact]
    public void Parse_ChecksumMatchesComputed()
    {
        FirmwareImage image = FirmwareImage.Parse(BuildImage());

        Assert.Equal(0xEB, image.Checksum);
        Assert.Equal(image.Checksum, image.ComputeChecksum());
    }

    [Fact]
    public void Parse_RejectsInvalidMagic()
    {
        Assert.Throws<FormatException>(() => FirmwareImage.Parse(new byte[24]));
    }

    [Fact]
    public void Parse_RejectsTruncatedSegment()
    {
        byte[] header = new byte[24];
        header[0] = 0xE9;
        header[1] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 0x40080000);
        byte[] segmentHeader = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(segmentHeader.AsSpan(4), 1000);

        Assert.Throws<FormatException>(() => FirmwareImage.Parse(header.Concat(segmentHeader).ToArray()));
    }
}
