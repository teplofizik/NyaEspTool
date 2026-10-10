using System.Buffers.Binary;
using System.Security.Cryptography;
using Esp32Flasher.Core;
using Esp32Flasher.Core.Chips;
using Esp32Flasher.Core.Errors;
using Esp32Flasher.Core.Loaders;
using Esp32Flasher.Core.Protocol;
using Esp32Flasher.Core.Reset;
using Esp32Flasher.Core.Tests.Fakes;

namespace Esp32Flasher.Core.Tests;

public class FlashTests
{
    private static readonly byte[] Ohai = "OHAI"u8.ToArray();

    private static StubBlob TestStub() => new()
    {
        Text = new byte[] { 0x01, 0x02, 0x03, 0x04 },
        TextStart = 0x400A8000,
        Data = new byte[] { 0x05, 0x06, 0x07, 0x08 },
        DataStart = 0x3FFDED6C,
        Entry = 0x400A902C,
    };

    private static EspLoader CreateLoader(FakeEspTransport transport) => new(transport, new EspConnectionOptions
    {
        Transport = transport,
        BeforeReset = ResetMode.NoReset,
        SyncTimeout = TimeSpan.FromMilliseconds(50),
        CommandTimeout = TimeSpan.FromMilliseconds(100),
        StubStartTimeout = TimeSpan.FromMilliseconds(200),
    });

    private static void RegisterConnect(FakeEspTransport transport)
    {
        transport.On(EspCommand.Sync, data: new byte[] { 0x00, 0x00, 0x00, 0x00 });
        transport.On(EspCommand.ReadReg, _ => new FakeResponse(Esp32Chip.MagicValue, new byte[] { 0x00, 0x00, 0x00, 0x00 }));
    }

    private static async Task<EspLoader> StartStubLoaderAsync(FakeEspTransport transport)
    {
        RegisterConnect(transport);
        transport.On(EspCommand.MemBegin, data: new byte[] { 0x00, 0x00 });
        transport.On(EspCommand.MemData, data: new byte[] { 0x00, 0x00 });
        transport.On(EspCommand.MemEnd, _ =>
        {
            transport.EnqueueFrame(Ohai);
            return new FakeResponse(0, new byte[] { 0x00, 0x00 });
        });
        transport.On(EspCommand.FlashBegin, data: new byte[] { 0x00, 0x00 });
        transport.On(EspCommand.FlashData, data: new byte[] { 0x00, 0x00 });
        transport.On(EspCommand.FlashEnd, data: new byte[] { 0x00, 0x00 });

        EspLoader loader = CreateLoader(transport);
        await loader.StartAsync();
        await loader.LoadStubAsync(TestStub());
        return loader;
    }

    [Fact]
    public async Task WriteFlash_Stub_SendsFiveWordFlashBegin()
    {
        var transport = new FakeEspTransport();
        EspLoader loader = await StartStubLoaderAsync(transport);
        byte[] data = new byte[20];

        await loader.WriteFlashAsync(
            new FlashImage(0x10000, data, "app.bin"),
            new FlashOptions { Verify = false, PacketSizeBytes = 16 });

        var begin = transport.Received.First(r => r.Command == EspCommand.FlashBegin);
        Assert.Equal(20, begin.Data.Length);
        Assert.Equal(20u, BinaryPrimitives.ReadUInt32LittleEndian(begin.Data.AsSpan(0)));
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(begin.Data.AsSpan(4)));
        Assert.Equal(16u, BinaryPrimitives.ReadUInt32LittleEndian(begin.Data.AsSpan(8)));
        Assert.Equal(0x10000u, BinaryPrimitives.ReadUInt32LittleEndian(begin.Data.AsSpan(12)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(begin.Data.AsSpan(16)));

        await loader.DisposeAsync();
    }

    [Fact]
    public async Task WriteFlash_PadsLastBlockWith0xFFAndSendsChecksum()
    {
        var transport = new FakeEspTransport();
        EspLoader loader = await StartStubLoaderAsync(transport);
        byte[] data = { 0xAA, 0xBB, 0xCC };

        await loader.WriteFlashAsync(
            new FlashImage(0x0, data, "x.bin"),
            new FlashOptions { Verify = false, PacketSizeBytes = 16 });

        var flashData = transport.Received.Where(r => r.Command == EspCommand.FlashData).ToList();
        var block = Assert.Single(flashData);
        Assert.Equal(16u, BinaryPrimitives.ReadUInt32LittleEndian(block.Data.AsSpan(0)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(block.Data.AsSpan(4)));

        byte[] payload = block.Data[16..];
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC, 0xFF }, payload[..4]);
        Assert.All(payload[4..], b => Assert.Equal(0xFF, b));
        Assert.Equal(EspChecksum.Compute(payload), (byte)block.Checksum);

        await loader.DisposeAsync();
    }

    [Fact]
    public async Task WriteFlash_Verify_PassesWhenMd5Matches()
    {
        var transport = new FakeEspTransport();
        EspLoader loader = await StartStubLoaderAsync(transport);
        byte[] data = { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
        byte[] md5 = MD5.HashData(data);
        transport.On(EspCommand.SpiFlashMd5, _ => new FakeResponse(0, md5.Concat(new byte[] { 0x00, 0x00 }).ToArray()));

        await loader.WriteFlashAsync(
            new FlashImage(0x10000, data, "app.bin"),
            new FlashOptions { Verify = true, PacketSizeBytes = 16 });

        Assert.Contains(transport.Received, r => r.Command == EspCommand.SpiFlashMd5);
        await loader.DisposeAsync();
    }

    [Fact]
    public async Task WriteFlash_Verify_ThrowsWhenMd5Mismatches()
    {
        var transport = new FakeEspTransport();
        EspLoader loader = await StartStubLoaderAsync(transport);
        byte[] data = { 0x01, 0x02, 0x03, 0x04 };
        transport.On(EspCommand.SpiFlashMd5, _ => new FakeResponse(0, new byte[18]));

        EspException exception = await Assert.ThrowsAsync<EspException>(() => loader.WriteFlashAsync(
            new FlashImage(0x10000, data, "app.bin"),
            new FlashOptions { Verify = true, PacketSizeBytes = 16 }));

        Assert.Equal(EspError.VerificationFailed, exception.Error);
        await loader.DisposeAsync();
    }

    [Fact]
    public async Task WriteFlash_Rom_SendsFourWordFlashBegin()
    {
        var transport = new FakeEspTransport();
        RegisterConnect(transport);
        transport.On(EspCommand.FlashBegin, data: new byte[] { 0x00, 0x00 });
        transport.On(EspCommand.FlashData, data: new byte[] { 0x00, 0x00 });
        EspLoader loader = CreateLoader(transport);
        await loader.StartAsync();

        await loader.WriteFlashAsync(
            new FlashImage(0x1000, new byte[4], "bootloader.bin"),
            new FlashOptions { Verify = false, PacketSizeBytes = 16 });

        var begin = transport.Received.First(r => r.Command == EspCommand.FlashBegin);
        Assert.Equal(16, begin.Data.Length);

        await loader.DisposeAsync();
    }
}
