# Интерфейс взаимодействия с приложением (Core API + CLI)

Цель: `Core` — переиспользуемая библиотека без зависимости от консоли; `Cli` — тонкая
обёртка. Любой host (CLI, будущий GUI, тесты) работает через один фасад.

## 1. Слои и пространства имён

```
Esp32Flasher.Core.Serial     ISerialTransport, SystemSerialTransport, SerialPortInfo
Esp32Flasher.Core.Slip       SlipCodec
Esp32Flasher.Core.Protocol   EspCommand, EspPacket, EspResponse, EspStatus, Checksum
Esp32Flasher.Core.Loaders    EspLoader (ROM), EspStubLoader, IEspLoader
Esp32Flasher.Core.Chips      IChip, Esp32Chip, ChipInfo, ChipDetector
Esp32Flasher.Core.Imaging    FirmwareImage, Segment, ImageMerger
Esp32Flasher.Core            Esp32Connection (фасад), FlashOptions, FlashImage, Progress
```

## 2. Транспорт

Отвязан от `System.IO.Ports` — легко мокается в тестах.

```csharp
public interface ISerialTransport : IAsyncDisposable
{
    bool IsOpen { get; }
    int BaudRate { get; set; }
    Task OpenAsync(CancellationToken ct = default);
    Task CloseAsync();

    // Линии авто-сброса
    bool Dtr { get; set; }
    bool Rts { get; set; }

    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default);
    ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default);
    void DiscardInBuffer();
    void DiscardOutBuffer();
}

public sealed record SerialPortInfo(string PortName, string? Description, string? HardwareId);

public static class SerialPorts
{
    public static IReadOnlyList<SerialPortInfo> List();
}
```

Реализация `SystemSerialTransport`:
- `Handshake.None`, `DtrEnable/RtsEnable` управляем явно;
- чтение с `ReadTimeout`/`ct` для отмены;
- опц. `Tracing`-хук (`Action<TraceDirection, ReadOnlyMemory<byte>>`) для `--trace`.

## 3. Фасад — главная точка входа

```csharp
public sealed class Esp32Connection : IAsyncDisposable
{
    public static Task<Esp32Connection> OpenAsync(
        EspConnectionOptions options,
        CancellationToken ct = default);

    public ChipInfo Chip { get; }
    public bool StubRunning { get; }

    // Диагностика
    public Task<ChipInfo> DetectChipAsync(CancellationToken ct = default);
    public Task<uint> ReadRegisterAsync(uint address, CancellationToken ct = default);
    public Task<FlashId> ReadFlashIdAsync(CancellationToken ct = default);

    // Прошивка
    public Task FlashAsync(
        IReadOnlyList<FlashImage> images,
        FlashOptions options,
        IProgress<FlashProgress>? progress = null,
        CancellationToken ct = default);

    public Task VerifyAsync(
        IReadOnlyList<FlashImage> images,
        IProgress<FlashProgress>? progress = null,
        CancellationToken ct = default);

    // Чтение/стирание
    public Task ReadFlashAsync(Stream output, uint offset, uint length,
        IProgress<FlashProgress>? progress = null, CancellationToken ct = default);

    public Task EraseFlashAsync(CancellationToken ct = default);
    public Task EraseRegionAsync(uint offset, uint length, CancellationToken ct = default);

    public Task HardResetAsync(CancellationToken ct = default);
}
```

## 4. Опции и DTO

```csharp
public sealed record EspConnectionOptions
{
    public required ISerialTransport Transport { get; init; }   // или PortName
    public uint InitialBaud { get; init; } = 115200;
    public uint? FlashBaud { get; init; } = 921600;             // null => не менять
    public ResetMode BeforeReset { get; init; } = ResetMode.Default; // or NoReset
    public ResetMode AfterReset { get; init; } = ResetMode.Hard;
    public bool LoadStub { get; init; } = true;                 // --no-stub => false
    public int SyncAttempts { get; init; } = 7;
    public TraceLevel Trace { get; init; } = TraceLevel.None;
}

public enum ResetMode { Default, Hard, NoReset, UsbJtag }

public sealed record FlashImage(uint Offset, byte[] Data, string? Name = null)
{
    public static FlashImage FromFile(uint offset, string path);
}

public sealed record FlashOptions
{
    public uint PacketSizeBytes { get; init; } = 16 * 1024;
    public bool Compress { get; init; } = false;               // FLASH_DEFL_*
    public bool Verify { get; init; } = true;                  // SPI_FLASH_MD5
    public FlashSize FlashSize { get; init; } = FlashSize.Detect;
    public SpiFlashMode FlashMode { get; init; } = SpiFlashMode.Keep;
    public FlashFrequency FlashFreq { get; init; } = FlashFrequency.Keep;
    public int MaxRetriesPerBlock { get; init; } = 3;
}

public sealed record ChipInfo(
    string Name,           // "ESP32-D0WDQ6-V3"
    uint MagicValue,
    int RevisionMajor,
    int RevisionMinor,
    byte[] Mac,
    uint FlashSizeBytes,
    FlashId FlashId);

public sealed record FlashId(byte Manufacturer, ushort MemoryType, byte Capacity,
    uint SizeBytes, string ManufacturerName);

public sealed record FlashProgress(
    FlashStage Stage,      // Connect, Sync, Stub, Erase, Write, Verify, Read, Reset
    long BytesDone,
    long BytesTotal,
    string? CurrentItem = null)
{
    public double Fraction => BytesTotal == 0 ? 0 : (double)BytesDone / BytesTotal;
}
```

## 5. Уровень протокола (внутренний, но публикуем для тестов/диагностики)

```csharp
public enum EspCommand : byte
{
    FlashBegin = 0x02, FlashData = 0x03, FlashEnd = 0x04,
    MemBegin   = 0x05, MemEnd    = 0x06, MemData   = 0x07,
    Sync       = 0x08, WriteReg  = 0x09, ReadReg   = 0x0A,
    SpiSetParams = 0x0B, SpiAttach = 0x0D, ChangeBaud = 0x0F,
    FlashDeflBegin = 0x10, FlashDeflData = 0x11, FlashDeflEnd = 0x12,
    SpiFlashMd5 = 0x13,
    EraseFlash = 0xD0, EraseRegion = 0xD1, ReadFlash = 0xD2, RunUserCode = 0xD3,
}

public static class SlipCodec
{
    public static byte[] Encode(ReadOnlySpan<byte> payload);      // обрамление + escape
    public sealed record DecodeResult(byte[] Packet, int Consumed);
    public static bool TryDecode(ReadOnlySpan<byte> buffer,
        out DecodeResult result, out int bytesNeeded);
}
```

`EspLoader` (ROM) и `EspStubLoader` реализуют:

```csharp
public interface IEspLoader
{
    Task<EspResponse> SendCommandAsync(EspCommand cmd, byte[]? data = null,
        uint? timeoutMs = null, CancellationToken ct = default);
    Task<uint> ReadRegAsync(uint address, CancellationToken ct = default);
    Task WriteRegAsync(uint address, uint value, uint mask = 0xFFFFFFFF, uint delayUs = 0, CancellationToken ct = default);
    Task SyncAsync(CancellationToken ct = default);
    Task<byte[]> ReadFlashBlockAsync(uint offset, int length, CancellationToken ct = default);
}
```

## 6. Абстракция чипа (расширяемость на S2/S3/C3)

```csharp
public interface IChip
{
    string Name { get; }
    uint MagicValue { get; }
    bool UsesStub { get; }
    ReadOnlyMemory<byte> GetStubBlob();     // данные stub_flasher_<chip>.json
    uint StubEntryPoint { get; }
    uint BootloaderOffset { get; }          // ESP32 => 0x1000
    SpiFlashParams DefaultSpiParams { get; }
    void ApplyImageHeader(byte[] image, SpiFlashMode mode, FlashFrequency freq, FlashSize size);
}
```

## 7. Пример использования Core (host-код)

```csharp
await using var link = new SystemSerialTransport("COM5");
await using var esp = await Esp32Connection.OpenAsync(new EspConnectionOptions
{
    Transport = link,
    FlashBaud = 921600,
    LoadStub = true,
});

Console.WriteLine($"{esp.Chip.Name}, {esp.Chip.FlashSizeBytes / 1024 / 1024} MB");

var progress = new Progress<FlashProgress>(p =>
    Console.Write($"\r{p.Stage}: {p.Fraction:P0}   "));

await esp.FlashAsync(
    new[]
    {
        FlashImage.FromFile(0x1000,  "bootloader.bin"),
        FlashImage.FromFile(0x8000,  "partitions.bin"),
        FlashImage.FromFile(0xe000,  "boot_app0.bin"),
        FlashImage.FromFile(0x10000, "firmware.bin"),
    },
    new FlashOptions { Compress = true, Verify = true, FlashSize = FlashSize.Detect },
    progress);

await esp.HardResetAsync();
```

## 8. CLI — контракт команд

```
esp32flash list                                   Список портов
esp32flash chip-info  --port COM5                 Чип, ревизия, MAC, flash
esp32flash flash-id   --port COM5                 JEDEC ID flash-чипа

esp32flash flash      --port COM5 [--chip esp32] [--baud 921600]
                      [-z|--compress] [--no-stub]
                      [--flash-size detect|4MB] [--flash-mode dio] [--flash-freq 80m]
                      [--verify|--no-verify] [--before default_reset|no_reset]
                      [--after hard_reset|no_reset]
                      <offset> <file> [<offset> <file> ...]

esp32flash read-flash --port COM5 <offset> <size> -o dump.bin
esp32flash erase-flash    --port COM5 [--force]
esp32flash erase-region   --port COM5 <offset> <size>
esp32flash reset          --port COM5
esp32flash --trace ...                            Дамп трафика «по проводу»
```

Пример для ESP32-CAM:

```
esp32flash flash --port COM5 --chip esp32 --baud 921600 -z \
  --flash-size detect --flash-mode dio --flash-freq 80m \
  0x1000 bootloader.bin 0x8000 partitions.bin 0xe000 boot_app0.bin 0x10000 firmware.bin
```

Возвращаемые коды: `0` — успех, `1` — ошибка операции, `2` — ошибка аргументов,
`3` — не найден порт/чип. Прогресс — в stderr, результат/логи — в stdout.

## 9. Модель ошибок

```csharp
public class EspException : Exception { public EspError Error { get; } }

public enum EspError
{
    PortNotFound, PortBusy, NoSync, ChipNotSupported,
    CommandFailed, RomError, StubError, ChecksumMismatch,
    FlashWriteFailed, VerificationFailed, Timeout, Cancelled,
}
```

Правила:
- Все операции `async` и принимают `CancellationToken`.
- Ретраи блоков записи внутри Core; наружу — только финальная ошибка с контекстом
  (offset, блок, k попытки).
- Никакого `Console.Write` в Core — только `IProgress<T>` и `ILogger`-совместимый хук.

## 10. Тестируемость

- `ISerialTransport` мокается: юнит-тесты на `SlipCodec`, checksum, packet round-trip.
- «Golden»-тесты парсинга ответов (сырые байты из `esptool --trace`).
- Интеграционный тест (опц., помеченный) — реальное железо на CI не гоняем.
- `FirmwareImage` тестируется на реальных `bootloader.bin`/`firmware.bin`.
