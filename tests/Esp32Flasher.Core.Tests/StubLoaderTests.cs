using System.Buffers.Binary;
using Esp32Flasher.Core;
using Esp32Flasher.Core.Chips;
using Esp32Flasher.Core.Loaders;
using Esp32Flasher.Core.Protocol;
using Esp32Flasher.Core.Reset;
using Esp32Flasher.Core.Tests.Fakes;

namespace Esp32Flasher.Core.Tests;

public class StubLoaderTests
{
    private static readonly byte[] Ohai = "OHAI"u8.ToArray();

    private static StubBlob TestStub() => new()
    {
        Text = new byte[] { 0x01, 0x02, 0x03, 0x04 },
        TextStart = 0x400A8000,
        Data = new byte[] { 0x05, 0x06, 0x07, 0x08 },
        DataStart = 0x3FFDED6C,
        BssStart = 0x3FFCC000,
        Entry = 0x400A902C,
    };

    private static async Task<EspLoader> StartLoaderAsync(FakeEspTransport transport)
    {
        transport.On(EspCommand.Sync, data: new byte[] { 0x00, 0x00, 0x00, 0x00 });
        transport.On(EspCommand.ReadReg, _ => new FakeResponse(Esp32Chip.MagicValue, new byte[] { 0x00, 0x00, 0x00, 0x00 }));
        transport.On(EspCommand.MemBegin, data: new byte[] { 0x00, 0x00 });
        transport.On(EspCommand.MemData, data: new byte[] { 0x00, 0x00 });
        transport.On(EspCommand.MemEnd, _ =>
        {
            transport.EnqueueFrame(Ohai);
            return new FakeResponse(0, new byte[] { 0x00, 0x00 });
        });

        var options = new EspConnectionOptions
        {
            Transport = transport,
            BeforeReset = ResetMode.NoReset,
            SyncTimeout = TimeSpan.FromMilliseconds(50),
            CommandTimeout = TimeSpan.FromMilliseconds(100),
            StubStartTimeout = TimeSpan.FromMilliseconds(200),
        };

        var loader = new EspLoader(transport, options);
        await loader.StartAsync();
        return loader;
    }

    [Fact]
    public async Task LoadStub_UploadsBothSegmentsAtTheirOffsets()
    {
        var transport = new FakeEspTransport();
        EspLoader loader = await StartLoaderAsync(transport);

        await loader.LoadStubAsync(TestStub());

        Assert.True(loader.IsStub);
        Assert.Contains(
            transport.Received,
            r => r.Command == EspCommand.MemBegin
                 && BinaryPrimitives.ReadUInt32LittleEndian(r.Data.AsSpan(12)) == 0x400A8000);
        Assert.Contains(
            transport.Received,
            r => r.Command == EspCommand.MemBegin
                 && BinaryPrimitives.ReadUInt32LittleEndian(r.Data.AsSpan(12)) == 0x3FFDED6C);

        await loader.DisposeAsync();
    }

    [Fact]
    public async Task LoadStub_MemDataCarriesPayloadAndChecksum()
    {
        var transport = new FakeEspTransport();
        EspLoader loader = await StartLoaderAsync(transport);

        await loader.LoadStubAsync(TestStub());

        var memData = transport.Received.Where(r => r.Command == EspCommand.MemData).ToList();
        Assert.Equal(2, memData.Count);

        var textBlock = memData[0];
        Assert.Equal(4u, BinaryPrimitives.ReadUInt32LittleEndian(textBlock.Data.AsSpan(0)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(textBlock.Data.AsSpan(4)));
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04 }, textBlock.Data[16..]);
        Assert.Equal(EspChecksum.Compute(new byte[] { 0x01, 0x02, 0x03, 0x04 }), (byte)textBlock.Checksum);

        await loader.DisposeAsync();
    }

    [Fact]
    public async Task ChangeBaud_WithStub_SendsOldBaudAsSecondArgument()
    {
        var transport = new FakeEspTransport();
        transport.On(EspCommand.ChangeBaud, data: new byte[] { 0x00, 0x00 });
        EspLoader loader = await StartLoaderAsync(transport);
        await loader.LoadStubAsync(TestStub());

        await loader.ChangeBaudAsync(921600);

        var change = transport.Received.Single(r => r.Command == EspCommand.ChangeBaud);
        Assert.Equal(921600u, BinaryPrimitives.ReadUInt32LittleEndian(change.Data.AsSpan(0)));
        Assert.Equal(115200u, BinaryPrimitives.ReadUInt32LittleEndian(change.Data.AsSpan(4)));
        Assert.Equal(921600, transport.BaudRate);

        await loader.DisposeAsync();
    }

    [Fact]
    public async Task ChangeBaud_WithoutStub_SendsZeroAsSecondArgument()
    {
        var transport = new FakeEspTransport();
        transport.On(EspCommand.ChangeBaud, data: new byte[] { 0x00, 0x00 });
        EspLoader loader = await StartLoaderAsync(transport);

        await loader.ChangeBaudAsync(921600);

        var change = transport.Received.Single(r => r.Command == EspCommand.ChangeBaud);
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(change.Data.AsSpan(4)));

        await loader.DisposeAsync();
    }

    [Fact]
    public void EmbeddedStub_Parses()
    {
        StubBlob stub = Esp32Stub.Load();

        Assert.NotEmpty(stub.Text);
        Assert.NotEmpty(stub.Data);
        Assert.NotEqual(0u, stub.Entry);
        Assert.InRange(stub.TextStart, 0x40000000u, 0x40400000u);
    }
}
