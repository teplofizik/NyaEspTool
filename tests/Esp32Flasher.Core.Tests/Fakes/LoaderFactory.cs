using Esp32Flasher.Core;
using Esp32Flasher.Core.Chips;
using Esp32Flasher.Core.Loaders;
using Esp32Flasher.Core.Protocol;
using Esp32Flasher.Core.Reset;

namespace Esp32Flasher.Core.Tests.Fakes;

internal static class LoaderFactory
{
    private static readonly byte[] Ohai = "OHAI"u8.ToArray();

    public static StubBlob Stub() => new()
    {
        Text = new byte[] { 0x01, 0x02, 0x03, 0x04 },
        TextStart = 0x400A8000,
        Data = new byte[] { 0x05, 0x06, 0x07, 0x08 },
        DataStart = 0x3FFDED6C,
        Entry = 0x400A902C,
    };

    public static void RegisterConnect(FakeEspTransport transport)
    {
        transport.On(EspCommand.Sync, data: new byte[] { 0x00, 0x00, 0x00, 0x00 });
        transport.On(EspCommand.ReadReg, _ => new FakeResponse(Esp32Chip.MagicValue, new byte[] { 0x00, 0x00, 0x00, 0x00 }));
    }

    public static async Task<EspLoader> StartStubAsync(FakeEspTransport transport)
    {
        RegisterConnect(transport);
        transport.On(EspCommand.MemBegin, data: new byte[] { 0x00, 0x00 });
        transport.On(EspCommand.MemData, data: new byte[] { 0x00, 0x00 });
        transport.On(EspCommand.MemEnd, _ =>
        {
            transport.EnqueueFrame(Ohai);
            return new FakeResponse(0, new byte[] { 0x00, 0x00 });
        });

        var loader = new EspLoader(transport, new EspConnectionOptions
        {
            Transport = transport,
            BeforeReset = ResetMode.NoReset,
            SyncTimeout = TimeSpan.FromMilliseconds(50),
            CommandTimeout = TimeSpan.FromMilliseconds(100),
            StubStartTimeout = TimeSpan.FromMilliseconds(200),
        });

        await loader.StartAsync();
        await loader.LoadStubAsync(Stub());
        return loader;
    }
}
