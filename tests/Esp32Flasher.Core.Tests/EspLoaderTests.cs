using System.Buffers.Binary;
using Esp32Flasher.Core.Chips;
using Esp32Flasher.Core.Errors;
using Esp32Flasher.Core.Loaders;
using Esp32Flasher.Core.Protocol;
using Esp32Flasher.Core.Reset;
using Esp32Flasher.Core.Tests.Fakes;

namespace Esp32Flasher.Core.Tests;

public class EspLoaderTests
{
    private static EspConnectionOptions Options(FakeEspTransport transport, int syncAttempts = 7) => new()
    {
        Transport = transport,
        BeforeReset = ResetMode.NoReset,
        SyncAttempts = syncAttempts,
        SyncTimeout = TimeSpan.FromMilliseconds(50),
        CommandTimeout = TimeSpan.FromMilliseconds(100),
    };

    private static void RegisterDefaultChip(FakeEspTransport transport)
    {
        transport.On(EspCommand.Sync, data: new byte[] { 0x00, 0x00, 0x00, 0x00 });
        transport.On(EspCommand.ReadReg, data =>
        {
            uint address = BinaryPrimitives.ReadUInt32LittleEndian(data);
            uint value = address switch
            {
                Esp32Chip.MagicAddress => Esp32Chip.MagicValue,
                Esp32Chip.EfuseBase + 8 => 0x00112233u,
                Esp32Chip.EfuseBase + 4 => 0x44556677u,
                _ => 0u,
            };
            return new FakeResponse(value, new byte[] { 0x00, 0x00, 0x00, 0x00 });
        });
    }

    [Fact]
    public async Task Start_SyncsAndDetectsChip()
    {
        var transport = new FakeEspTransport();
        RegisterDefaultChip(transport);
        var loader = new EspLoader(transport, Options(transport));

        await loader.StartAsync();

        Assert.NotNull(loader.Chip);
        Assert.Equal("ESP32-D0WDQ6", loader.Chip!.Name);
        Assert.Equal(0, loader.Chip.RevisionMajor);
        Assert.Equal(0, loader.Chip.RevisionMinor);
        Assert.Equal("22:33:44:55:66:77", loader.Chip.MacAddress);

        await loader.DisposeAsync();
    }

    [Fact]
    public async Task Sync_ThrowsNoSync_WhenNoResponse()
    {
        var transport = new FakeEspTransport();
        var loader = new EspLoader(transport, Options(transport, syncAttempts: 1));

        EspException exception = await Assert.ThrowsAsync<EspException>(() => loader.StartAsync());

        Assert.Equal(EspError.NoSync, exception.Error);
        await loader.DisposeAsync();
    }

    [Fact]
    public async Task DetectChip_Throws_WhenMagicUnsupported()
    {
        var transport = new FakeEspTransport();
        transport.On(EspCommand.Sync, data: new byte[] { 0x00, 0x00, 0x00, 0x00 });
        transport.On(EspCommand.ReadReg, 0xDEADBEEFu, new byte[] { 0x00, 0x00, 0x00, 0x00 });
        var loader = new EspLoader(transport, Options(transport));

        EspException exception = await Assert.ThrowsAsync<EspException>(() => loader.StartAsync());

        Assert.Equal(EspError.ChipNotSupported, exception.Error);
        await loader.DisposeAsync();
    }

    [Fact]
    public async Task Start_SendsFramedSyncAndReadRegCommands()
    {
        var transport = new FakeEspTransport();
        RegisterDefaultChip(transport);
        var loader = new EspLoader(transport, Options(transport));

        await loader.StartAsync();

        Assert.Contains(transport.Received, r => r.Command == EspCommand.Sync);
        Assert.Contains(
            transport.Received,
            r => r.Command == EspCommand.ReadReg
                 && BinaryPrimitives.ReadUInt32LittleEndian(r.Data) == Esp32Chip.MagicAddress);

        await loader.DisposeAsync();
    }

    [Fact]
    public async Task Start_TogglesResetLines_WhenDefaultReset()
    {
        var transport = new FakeEspTransport();
        RegisterDefaultChip(transport);
        EspConnectionOptions options = Options(transport) with { BeforeReset = ResetMode.Default };
        var loader = new EspLoader(transport, options);

        await loader.StartAsync();

        Assert.Contains(transport.LineChanges, c => c.Dtr && !c.Rts);
        Assert.False(transport.Dtr);

        await loader.DisposeAsync();
    }
}
