using System.Buffers.Binary;
using System.Security.Cryptography;
using Esp32Flasher.Core;
using Esp32Flasher.Core.Chips;
using Esp32Flasher.Core.Imaging;
using Esp32Flasher.Core.Loaders;
using Esp32Flasher.Core.Protocol;
using Esp32Flasher.Core.Reset;
using Esp32Flasher.Core.Tests.Fakes;

namespace Esp32Flasher.Core.Tests;

public class FlashModeTests
{
    private static byte[] BuildBootloader(bool hashAppended)
    {
        byte[] header = new byte[24];
        header[0] = 0xE9;
        header[1] = 1;
        header[2] = (byte)FlashMode.Dio;
        header[3] = 0x2F;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 0x40080000);
        header[23] = (byte)(hashAppended ? 1 : 0);

        byte[] segment = { 0x01, 0x02, 0x03, 0x04 };
        byte[] segmentHeader = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(segmentHeader.AsSpan(0), 0x40078000);
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
        if (hashAppended)
        {
            body.AddRange(new byte[32]);
        }

        return body.ToArray();
    }

    [Fact]
    public void Apply_SetsModeFreqSizeAndRecomputesSha()
    {
        byte[] image = BuildBootloader(hashAppended: true);

        byte[] patched = ImageFlashParams.Apply(
            image, 0x1000, FlashMode.Qio, FlashFrequency.MHz40, FlashSize.MB8, 0x1000);

        Assert.Equal((byte)FlashMode.Qio, patched[2]);
        Assert.Equal(0x30, patched[3]);

        int dataLength = patched.Length - 32;
        byte[] expected = SHA256.HashData(patched.AsSpan(0, dataLength));
        Assert.Equal(expected, patched[dataLength..]);
    }

    [Fact]
    public void Apply_AllKeep_ReturnsSameInstance()
    {
        byte[] image = BuildBootloader(hashAppended: false);

        byte[] result = ImageFlashParams.Apply(image, 0x1000, null, null, null, 0x1000);

        Assert.Same(image, result);
    }

    [Fact]
    public void Apply_NoChange_ReturnsSameInstance()
    {
        byte[] image = BuildBootloader(hashAppended: false);

        byte[] result = ImageFlashParams.Apply(
            image, 0x1000, FlashMode.Dio, FlashFrequency.MHz80, FlashSize.MB4, 0x1000);

        Assert.Same(image, result);
    }

    [Fact]
    public void Apply_NonBootloaderOffset_ReturnsSameInstance()
    {
        byte[] image = BuildBootloader(hashAppended: false);

        byte[] result = ImageFlashParams.Apply(
            image, 0x10000, FlashMode.Qio, FlashFrequency.MHz40, FlashSize.MB8, 0x1000);

        Assert.Same(image, result);
    }

    [Fact]
    public void DecodeFlashSizeBytes_MapsJedecCapacity()
    {
        Assert.Equal(4u * 1024 * 1024, Esp32Chip.DecodeFlashSizeBytes(0x001640C8));
        Assert.Equal(8u * 1024 * 1024, Esp32Chip.DecodeFlashSizeBytes(0x001740C8));
        Assert.Null(Esp32Chip.DecodeFlashSizeBytes(0x00FF00C8));
    }

    [Fact]
    public void SizeFromBytes_MapsToNibble()
    {
        Assert.Equal(FlashSize.MB4, Esp32Chip.SizeFromBytes(4 * 1024 * 1024));
        Assert.Equal(FlashSize.MB8, Esp32Chip.SizeFromBytes(8 * 1024 * 1024));
    }

    [Fact]
    public async Task ReadFlashId_ReturnsSpiW0ValueAndSize()
    {
        var transport = new FakeEspTransport();
        const uint flashId = 0x001640C8;
        transport.On(EspCommand.Sync, data: new byte[] { 0x00, 0x00, 0x00, 0x00 });
        transport.On(EspCommand.ReadReg, data =>
        {
            uint address = BinaryPrimitives.ReadUInt32LittleEndian(data);
            uint value = address switch
            {
                Esp32Chip.MagicAddress => Esp32Chip.MagicValue,
                Esp32Chip.SpiRegBase + Esp32Chip.SpiW0Offs => flashId,
                _ => 0u,
            };
            return new FakeResponse(value, new byte[] { 0x00, 0x00, 0x00, 0x00 });
        });
        transport.On(EspCommand.WriteReg, data: new byte[] { 0x00, 0x00 });

        var loader = new EspLoader(transport, new EspConnectionOptions
        {
            Transport = transport,
            BeforeReset = ResetMode.NoReset,
            CommandTimeout = TimeSpan.FromMilliseconds(100),
        });
        await loader.StartAsync();

        uint id = await loader.ReadFlashIdAsync();

        Assert.Equal(flashId, id);
        Assert.Equal(4u * 1024 * 1024, Esp32Chip.DecodeFlashSizeBytes(id));
        await loader.DisposeAsync();
    }
}
