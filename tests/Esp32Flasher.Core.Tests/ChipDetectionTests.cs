using System.Buffers.Binary;
using Esp32Flasher.Core;
using Esp32Flasher.Core.Chips;
using Esp32Flasher.Core.Loaders;
using Esp32Flasher.Core.Protocol;
using Esp32Flasher.Core.Reset;
using Esp32Flasher.Core.Tests.Fakes;

namespace Esp32Flasher.Core.Tests;

public class ChipDetectionTests
{
    private static async Task<EspLoader> DetectAsync(FakeEspTransport transport, uint chipId)
    {
        transport.On(EspCommand.Sync, data: new byte[] { 0x00, 0x00, 0x00, 0x00 });
        byte[] securityInfo = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(securityInfo.AsSpan(12), chipId);
        transport.On(EspCommand.GetSecurityInfo, _ => new FakeResponse(0, securityInfo));
        transport.On(EspCommand.ReadReg, data: new byte[] { 0x00, 0x00, 0x00, 0x00 });

        var loader = new EspLoader(transport, new EspConnectionOptions
        {
            Transport = transport,
            BeforeReset = ResetMode.NoReset,
            CommandTimeout = TimeSpan.FromMilliseconds(100),
        });
        await loader.StartAsync();
        return loader;
    }

    [Theory]
    [InlineData(5u, "ESP32-C3")]
    [InlineData(9u, "ESP32-S3")]
    [InlineData(13u, "ESP32-C6")]
    public async Task DetectChip_FromSecurityInfo(uint chipId, string expectedName)
    {
        var transport = new FakeEspTransport();

        EspLoader loader = await DetectAsync(transport, chipId);

        Assert.Equal(expectedName, loader.Chip!.Name);
        Assert.True(loader.ChipConfig.UsesSecurityInfo);
        await loader.DisposeAsync();
    }

    [Fact]
    public async Task DetectChip_S3_UsesZeroBootloaderOffsetAndOwnSpiBase()
    {
        var transport = new FakeEspTransport();

        EspLoader loader = await DetectAsync(transport, 9);

        Assert.Equal(ChipRegistry.Esp32S3, loader.ChipConfig);
        Assert.Equal(0x0u, loader.ChipConfig.BootloaderOffset);
        Assert.Equal(0x60002000u, loader.ChipConfig.SpiRegBase);
        await loader.DisposeAsync();
    }

    [Fact]
    public void Registry_LooksUpByChipIdAndMagic()
    {
        Assert.Equal(ChipRegistry.Esp32S3, ChipRegistry.FromChipId(9));
        Assert.Equal(ChipRegistry.Esp32C6, ChipRegistry.FromChipId(13));
        Assert.Equal(ChipRegistry.Esp32C3, ChipRegistry.FromChipId(5));
        Assert.Equal(ChipRegistry.Esp32, ChipRegistry.FromMagic(0x00F01D83));
        Assert.Null(ChipRegistry.FromMagic(0x12345678));
        Assert.Null(ChipRegistry.FromChipId(0));
    }

    [Theory]
    [InlineData("Esp32Flasher.Core.Resources.esp32_stub.json")]
    [InlineData("Esp32Flasher.Core.Resources.esp32s3_stub.json")]
    [InlineData("Esp32Flasher.Core.Resources.esp32c6_stub.json")]
    [InlineData("Esp32Flasher.Core.Resources.esp32c3_stub.json")]
    public void StubBlobs_LoadAll(string resourceName)
    {
        StubBlob blob = StubBlobs.Load(resourceName);

        Assert.NotEmpty(blob.Text);
        Assert.NotEmpty(blob.Data);
        Assert.NotEqual(0u, blob.Entry);
    }
}
