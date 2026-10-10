# Core API и CLI (актуальный интерфейс)

`Core` — переиспользуемая библиотека без зависимости от консоли; `Cli` — тонкая обёртка.
Ниже — фактический публичный интерфейс.

## Пространства имён

```
Esp32Flasher.Core.Serial     ISerialTransport, SystemSerialTransport, SerialPorts, SerialPortInfo
Esp32Flasher.Core.Slip       SlipCodec, SlipDecoder
Esp32Flasher.Core.Protocol   EspCommand, EspPacket, EspResponse, EspChecksum, EspResponseStatus
Esp32Flasher.Core.Reset      ResetMode, ResetSequence
Esp32Flasher.Core.Loaders    FrameReader, EspLoader, EspTraceEntry, EspTraceDirection
Esp32Flasher.Core.Chips      ChipConfig, ChipRegistry, ChipInfo, StubBlob, StubBlobs
Esp32Flasher.Core.Imaging    FirmwareImage, ImageSegment, FlashMode, FlashFrequency, FlashSize, ImageFlashParams
Esp32Flasher.Core.Errors     EspError, EspException
Esp32Flasher.Core            Esp32Connection, EspConnectionOptions, FlashImage, FlashOptions, FlashProgress
```

## Транспорт

```csharp
public interface ISerialTransport : IAsyncDisposable
{
    bool IsOpen { get; }
    int BaudRate { get; set; }
    bool Dtr { get; set; }
    bool Rts { get; set; }
    Task OpenAsync(CancellationToken ct = default);
    Task CloseAsync();
    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default);
    ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default);
    void DiscardInBuffer();
    void DiscardOutBuffer();
}

public static class SerialPorts { public static IReadOnlyList<SerialPortInfo> List(); }
```

`SystemSerialTransport` реализует интерфейс через `System.IO.Ports` (`Handshake.None`,
явное управление DTR/RTS). Отвязка от `System.IO.Ports` позволяет мокать транспорт в тестах.

## Фасад

```csharp
public sealed class Esp32Connection : IAsyncDisposable
{
    public static Task<Esp32Connection> OpenAsync(EspConnectionOptions options, CancellationToken ct = default);

    public ChipInfo Chip { get; }
    public bool StubRunning { get; }
    public event Action<EspTraceEntry>? Traced;

    public Task<uint> ReadRegisterAsync(uint address, CancellationToken ct = default);
    public Task<uint> ReadFlashIdAsync(CancellationToken ct = default);
    public Task<uint?> DetectFlashSizeAsync(CancellationToken ct = default);

    public Task FlashAsync(IReadOnlyList<FlashImage> images, FlashOptions options,
        IProgress<FlashProgress>? progress = null, CancellationToken ct = default);

    public Task<byte[]> ReadFlashAsync(uint offset, uint length,
        IProgress<FlashProgress>? progress = null, CancellationToken ct = default);

    public Task EraseFlashAsync(CancellationToken ct = default);
    public Task EraseRegionAsync(uint offset, uint size, CancellationToken ct = default);
    public Task HardResetAsync(CancellationToken ct = default);
}
```

`OpenAsync` открывает порт, входит в bootloader (reset), делает `SYNC`, определяет чип,
грузит stub и (при заданном `FlashBaud`) повышает скорость.

## Опции и DTO

```csharp
public sealed record EspConnectionOptions
{
    public ISerialTransport? Transport { get; init; }
    public string? PortName { get; init; }
    public int InitialBaud { get; init; } = 115200;
    public int? FlashBaud { get; init; } = 921600;   // null => не менять скорость
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

public enum ResetMode { Default, Hard, NoReset, UsbJtag }

public sealed record FlashImage(uint Offset, byte[] Data, string? Name = null)
{
    public static FlashImage FromFile(uint offset, string path);
}

public sealed record FlashOptions
{
    public uint PacketSizeBytes { get; init; } = 16 * 1024;
    public uint FlashSizeBytes { get; init; } = 4 * 1024 * 1024;
    public bool Verify { get; init; } = true;
    public bool Reboot { get; init; } = true;
    public int MaxRetriesPerBlock { get; init; } = 3;
    public Imaging.FlashMode? Mode { get; init; }          // патчинг заголовка bootloader
    public Imaging.FlashFrequency? Frequency { get; init; }
    public Imaging.FlashSize? Size { get; init; }
}

public enum FlashStage { Connect, Sync, Stub, Erase, Write, Verify, Read, Reset }

public sealed record FlashProgress(FlashStage Stage, long BytesDone, long BytesTotal, string? CurrentItem = null)
{
    public double Fraction => BytesTotal <= 0 ? 0 : (double)BytesDone / BytesTotal;
}
```

## Чипы

```csharp
public sealed record ChipInfo(string Name, int RevisionMajor, int RevisionMinor, byte[] Mac)
{
    public string MacAddress { get; }
}

public sealed record ChipConfig
{
    public string Name { get; init; }
    public uint ImageChipId { get; init; }
    public uint? MagicValue { get; init; }
    public uint BootloaderOffset { get; init; }
    public uint SpiRegBase { get; init; }
    public int SpiUsrOffs { get; init; }
    public int SpiUsr2Offs { get; init; }
    public int SpiMisoDlenOffs { get; init; }
    public int SpiW0Offs { get; init; }
    public string StubResourceName { get; init; }
    public bool UsesSecurityInfo { get; init; }
    public MacReadMode MacMode { get; init; }
    public RevisionScheme Revision { get; init; }
    public uint MacRegister { get; init; }
    public uint EfuseReadBase { get; init; }
    public uint EfuseBase { get; init; }
    public int RamBlockSize { get; init; } = 0x1800;
}

public static class ChipRegistry
{
    public static ChipConfig Esp32 { get; }
    public static ChipConfig Esp32S3 { get; }
    public static ChipConfig Esp32C6 { get; }
    public static ChipConfig Esp32C3 { get; }
    public static IReadOnlyList<ChipConfig> All { get; }
    public static ChipConfig? FromMagic(uint magicValue);
    public static ChipConfig? FromChipId(uint chipId);
}

public static class StubBlobs { public static StubBlob Load(string resourceName); }
```

## Низкоуровневый загрузчик (для тестов/диагностики)

```csharp
public sealed class EspLoader : IAsyncDisposable
{
    public ChipInfo? Chip { get; }
    public ChipConfig ChipConfig { get; }
    public bool IsStub { get; }
    public event Action<EspTraceEntry>? Traced;

    public Task StartAsync(CancellationToken ct = default);          // reset + sync + detect
    public Task SyncAsync(CancellationToken ct = default);
    public Task<ChipInfo> DetectChipAsync(CancellationToken ct = default);

    public Task<uint> ReadRegAsync(uint address, CancellationToken ct = default);
    public Task WriteRegAsync(uint address, uint value, uint mask = 0xFFFFFFFF, uint delayUs = 0, CancellationToken ct = default);
    public Task<uint> ReadFlashIdAsync(CancellationToken ct = default);
    public Task<uint> RunSpiFlashCommandAsync(byte command, int readBits, CancellationToken ct = default);
    public Task<EspResponse> SendCommandAsync(EspCommand command, byte[]? data = null, byte checksum = 0,
        TimeSpan? timeout = null, CancellationToken ct = default);

    public Task LoadStubAsync(StubBlob stub, CancellationToken ct = default);
    public Task ChangeBaudAsync(int baudRate, CancellationToken ct = default);
    public Task SpiAttachAsync(CancellationToken ct = default);
    public Task SetSpiParamsAsync(uint flashSizeBytes, CancellationToken ct = default);

    public Task WriteFlashAsync(FlashImage image, FlashOptions options,
        IProgress<FlashProgress>? progress = null, CancellationToken ct = default);
    public Task FlashBeginAsync(uint eraseSize, uint offset, uint blockSize, uint numBlocks, CancellationToken ct = default);
    public Task FlashFinishAsync(bool reboot, CancellationToken ct = default);
    public Task<byte[]> FlashMd5Async(uint offset, uint size, CancellationToken ct = default);

    public Task EraseFlashAsync(CancellationToken ct = default);
    public Task EraseRegionAsync(uint offset, uint size, CancellationToken ct = default);
    public Task<byte[]> ReadFlashAsync(uint offset, uint length,
        IProgress<FlashProgress>? progress = null, CancellationToken ct = default);

    public Task HardResetAsync(CancellationToken ct = default);
    public void Stop();
}
```

`EspTraceEntry(EspTraceDirection Direction, byte[] Data)`, `EspTraceDirection { Tx, Rx }`.

## Пример использования

```csharp
using Esp32Flasher.Core;
using Esp32Flasher.Core.Imaging;

await using var esp = await Esp32Connection.OpenAsync(new EspConnectionOptions
{
    PortName = "COM23",
    FlashBaud = 921600,
    LoadStub = true,
});

Console.WriteLine($"{esp.Chip.Name} v{esp.Chip.RevisionMajor}.{esp.Chip.RevisionMinor}, {esp.Chip.MacAddress}");

var progress = new Progress<FlashProgress>(p => Console.Error.Write($"\r{p.Stage}: {p.Fraction:P0}   "));

await esp.FlashAsync(
    new[]
    {
        FlashImage.FromFile(0x1000, "bootloader.bin"),
        FlashImage.FromFile(0x8000, "partitions.bin"),
        FlashImage.FromFile(0xe000, "boot_app0.bin"),
        FlashImage.FromFile(0x10000, "firmware.bin"),
    },
    new FlashOptions
    {
        Verify = true,
        Size = FlashSize.MB4,
        Mode = FlashMode.Dio,
        Frequency = FlashFrequency.MHz80,
    },
    progress);
```

Для USB-Serial-JTAG: `BeforeReset = ResetMode.UsbJtag`.

## CLI — контракт

```
esp32flash list
esp32flash chip-info  --port <p> [--baud N] [--trace] [--before ...] [--no-stub]
esp32flash flash-id   --port <p> [--before ...]
esp32flash flash      --port <p> [--baud N] [--flash-baud N]
                      [--flash-size keep|detect|1MB|2MB|4MB|8MB|16MB]
                      [--flash-mode keep|qio|qout|dio|dout]
                      [--flash-freq keep|80m|40m|26m|20m]
                      [--no-stub] [--no-verify] [--before default_reset|usb_reset|no_reset]
                      [--chip <name>] <offset> <file> [<offset> <file> ...]
esp32flash read-flash --port <p> <offset> <size> [-o <file>]
esp32flash erase-flash  --port <p> --force
esp32flash erase-region --port <p> <offset> <size>
esp32flash image-info <file>
```

Коды возврата: `0` успех, `1` ошибка операции, `2` ошибка аргументов.

## Модель ошибок

```csharp
public enum EspError
{
    PortNotFound, PortBusy, NoSync, ChipNotSupported, CommandFailed, RomError,
    StubError, ChecksumMismatch, FlashWriteFailed, VerificationFailed, Timeout, Cancelled,
}

public sealed class EspException : Exception { public EspError Error { get; } }
```

Все операции — `async` и принимают `CancellationToken`. В `Core` нет `Console`; прогресс идёт
через `IProgress<T>`, трассировка — через событие `Traced` / `EspConnectionOptions.Trace`.

## Тестируемость

- `ISerialTransport` мокается (в тестах — `FakeEspTransport` с «отвечателями» на команды).
- `SlipCodec`/`EspChecksum`/`EspPacket`/`EspResponse` — юнит-тесты, в т.ч. golden-байты.
- Детект чипов (S3/C3/C6), загрузка stub, flash-команды, парсинг образов — покрыты тестами.
