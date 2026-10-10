using System.Buffers.Binary;
using System.Security.Cryptography;
using Esp32Flasher.Core.Errors;
using Esp32Flasher.Core.Loaders;
using Esp32Flasher.Core.Protocol;
using Esp32Flasher.Core.Slip;
using Esp32Flasher.Core.Tests.Fakes;

namespace Esp32Flasher.Core.Tests;

public class ReadEraseTests
{
    [Fact]
    public async Task EraseFlash_SendsCommand()
    {
        var transport = new FakeEspTransport();
        transport.On(EspCommand.EraseFlash, data: new byte[] { 0x00, 0x00 });
        EspLoader loader = await LoaderFactory.StartStubAsync(transport);

        await loader.EraseFlashAsync();

        Assert.Contains(transport.Received, r => r.Command == EspCommand.EraseFlash);
        await loader.DisposeAsync();
    }

    [Fact]
    public async Task EraseRegion_SendsOffsetAndSize()
    {
        var transport = new FakeEspTransport();
        transport.On(EspCommand.EraseRegion, data: new byte[] { 0x00, 0x00 });
        EspLoader loader = await LoaderFactory.StartStubAsync(transport);

        await loader.EraseRegionAsync(0x1000, 0x2000);

        var erase = transport.Received.Single(r => r.Command == EspCommand.EraseRegion);
        Assert.Equal(0x1000u, BinaryPrimitives.ReadUInt32LittleEndian(erase.Data.AsSpan(0)));
        Assert.Equal(0x2000u, BinaryPrimitives.ReadUInt32LittleEndian(erase.Data.AsSpan(4)));
        await loader.DisposeAsync();
    }

    [Fact]
    public async Task ReadFlash_AssemblesDataAndVerifiesMd5()
    {
        var transport = new FakeEspTransport();
        byte[] data = { 0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80 };
        transport.On(EspCommand.ReadFlash, _ => new FakeResponse(
            0, new byte[] { 0x00, 0x00 }, new[] { data, MD5.HashData(data) }));
        EspLoader loader = await LoaderFactory.StartStubAsync(transport);

        byte[] result = await loader.ReadFlashAsync(0x0, (uint)data.Length);

        Assert.Equal(data, result);
        await loader.DisposeAsync();
    }

    [Fact]
    public async Task ReadFlash_SendsAcknowledgementWithCumulativeLength()
    {
        var transport = new FakeEspTransport();
        byte[] data = { 0x01, 0x02, 0x03, 0x04 };
        transport.On(EspCommand.ReadFlash, _ => new FakeResponse(
            0, new byte[] { 0x00, 0x00 }, new[] { data, MD5.HashData(data) }));
        EspLoader loader = await LoaderFactory.StartStubAsync(transport);

        await loader.ReadFlashAsync(0x0, (uint)data.Length);

        Assert.True(
            transport.RawWrites.Any(w =>
                SlipCodec.TryDecode(w, out byte[] p, out _)
                && p.Length == 4
                && BinaryPrimitives.ReadUInt32LittleEndian(p) == (uint)data.Length),
            "Expected a 4-byte SLIP ack carrying the cumulative byte count.");

        await loader.DisposeAsync();
    }

    [Fact]
    public async Task ReadFlash_ThrowsWhenDigestMismatches()
    {
        var transport = new FakeEspTransport();
        byte[] data = { 0x01, 0x02, 0x03, 0x04 };
        transport.On(EspCommand.ReadFlash, _ => new FakeResponse(
            0, new byte[] { 0x00, 0x00 }, new[] { data, new byte[16] }));
        EspLoader loader = await LoaderFactory.StartStubAsync(transport);

        EspException exception = await Assert.ThrowsAsync<EspException>(
            () => loader.ReadFlashAsync(0x0, (uint)data.Length));

        Assert.Equal(EspError.VerificationFailed, exception.Error);
        await loader.DisposeAsync();
    }

    [Fact]
    public async Task ReadFlash_WithoutStub_Throws()
    {
        var transport = new FakeEspTransport();
        LoaderFactory.RegisterConnect(transport);
        var loader = new EspLoader(transport, new EspConnectionOptions
        {
            Transport = transport,
            BeforeReset = Esp32Flasher.Core.Reset.ResetMode.NoReset,
        });
        await loader.StartAsync();

        await Assert.ThrowsAsync<EspException>(() => loader.ReadFlashAsync(0x0, 16));
        await loader.DisposeAsync();
    }
}
