using Esp32Flasher.Core.Loaders;
using Esp32Flasher.Core.Reset;
using Esp32Flasher.Core.Serial;

namespace Esp32Flasher.Core;

public sealed record EspConnectionOptions
{
    public ISerialTransport? Transport { get; init; }

    public string? PortName { get; init; }

    public int InitialBaud { get; init; } = 115200;

    public int? FlashBaud { get; init; } = 921600;

    public ResetMode BeforeReset { get; init; } = ResetMode.Default;

    public ResetMode AfterReset { get; init; } = ResetMode.Hard;

    public bool LoadStub { get; init; } = true;

    public int SyncAttempts { get; init; } = 7;

    public TimeSpan SyncTimeout { get; init; } = TimeSpan.FromMilliseconds(100);

    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(3);

    public TimeSpan StubStartTimeout { get; init; } = TimeSpan.FromSeconds(3);

    public TimeSpan ResetDelay { get; init; } = TimeSpan.FromMilliseconds(50);

    public Action<EspTraceEntry>? Trace { get; init; }
}
